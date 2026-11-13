using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Always Awake")]
[assembly: System.Reflection.AssemblyProduct("Always Awake")]

static class Native {
    [DllImport("kernel32.dll")] internal static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll", SetLastError=true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LASTINPUT info);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    internal static bool SessionLocked() {
        IntPtr desktop=OpenInputDesktop(0,false,1);
        if(desktop==IntPtr.Zero) return true;
        CloseDesktop(desktop); return false;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct LASTINPUT { internal uint size, tick; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { internal uint type; internal UNION data; }
    [StructLayout(LayoutKind.Explicit)] internal struct UNION {
        [FieldOffset(0)] internal KEYBOARD keyboard;
        [FieldOffset(0)] internal MOUSE mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBOARD { internal ushort vk, scan; internal uint flags, time; internal UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSE { internal int x, y; internal uint mouseData, flags, time; internal UIntPtr extra; }
    internal const uint Continuous=0x80000000, SystemRequired=1, DisplayRequired=2;
    internal static bool Power(bool enabled, bool display) {
        return SetThreadExecutionState(Continuous | (enabled ? SystemRequired | (display ? DisplayRequired : 0) : 0)) != 0;
    }
    internal static uint IdleMilliseconds() {
        LASTINPUT info = new LASTINPUT(); info.size=(uint)Marshal.SizeOf(typeof(LASTINPUT));
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount-info.tick);
    }
    internal static bool Pulse() {
        INPUT input = new INPUT(); input.type=1;
        // Like Caffeine's default: F15 key-up only, with no held key and no cursor movement.
        input.data.keyboard.vk=0x7E; input.data.keyboard.flags=2;
        return SendInput(1,new INPUT[]{input},Marshal.SizeOf(typeof(INPUT)))==1;
    }
}

sealed class AwakeSettings {
    internal bool Enabled, Input=true, Display=true, Resume=true;
    internal int Minutes;
    internal long UntilUtc;
    internal static AwakeSettings Load(string path) {
        AwakeSettings s=new AwakeSettings();
        try {
            if(!File.Exists(path)) return s;
            foreach(string line in File.ReadAllLines(path)) {
                string[] pair=line.Split(new char[]{'='},2); if(pair.Length!=2) continue;
                bool b; int n; long t;
                switch(pair[0]) {
                    case "enabled": if(bool.TryParse(pair[1],out b)) s.Enabled=b; break;
                    case "input": if(bool.TryParse(pair[1],out b)) s.Input=b; break;
                    case "display": if(bool.TryParse(pair[1],out b)) s.Display=b; break;
                    case "resume": if(bool.TryParse(pair[1],out b)) s.Resume=b; break;
                    case "minutes": if(int.TryParse(pair[1],out n) && Array.IndexOf(new int[]{0,10,15,25,30,60,120},n)>=0) s.Minutes=n; break;
                    case "untilUtc": if(long.TryParse(pair[1],out t) && t>=0 && t<=DateTime.MaxValue.Ticks) s.UntilUtc=t; break;
                }
            }
            // Missing or invalid finite deadlines must never become indefinite sessions.
            if(s.Minutes!=0 && (s.UntilUtc==0 || s.UntilUtc<=DateTime.UtcNow.Ticks)) s.Enabled=false;
            return s;
        } catch { return new AwakeSettings(); }
    }
    internal void Save(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp=path+".tmp";
        File.WriteAllLines(temp,new string[]{"enabled="+Enabled,"minutes="+Minutes,"input="+Input,"display="+Display,"resume="+Resume,"untilUtc="+UntilUtc});
        if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
    }
}

sealed class TrayApp : ApplicationContext {
    readonly AwakeSettings settings;
    readonly string settingsPath;
    readonly Control dispatcher = new Control();
    readonly Icon onIcon=MakeIcon(Color.FromArgb(20,160,95));
    readonly Icon offIcon=MakeIcon(Color.FromArgb(110,115,125));
    readonly NotifyIcon tray = new NotifyIcon();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly ToolStripMenuItem stateItem = new ToolStripMenuItem();
    readonly ToolStripMenuItem toggleItem = new ToolStripMenuItem();
    readonly ToolStripMenuItem inputItem = new ToolStripMenuItem("30초마다 활동 입력 (F15)");
    readonly ToolStripMenuItem displayItem = new ToolStripMenuItem("화면도 켜 두기");
    readonly ToolStripMenuItem resumeItem = new ToolStripMenuItem("잠금 해제 후 자동 재개");
    readonly ToolStripMenuItem loginItem = new ToolStripMenuItem("Windows 로그인 시 자동 실행");
    readonly ToolStripMenuItem[] durationItems;
    readonly int[] choices = {10,15,25,30,60,120,0};
    bool active, locked, suspended, desired, ready, disposed;
    int minutes;
    long deadline, nextPulse;
    int pulses;
    internal bool Active { get { return active; } }
    internal bool SelfTest;
    internal int TimerPass;
    static Icon MakeIcon(Color color) {
        using(Bitmap bitmap=new Bitmap(16,16)) {
            using(Graphics g=Graphics.FromImage(bitmap)) {
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(SolidBrush brush=new SolidBrush(color)) g.FillEllipse(brush,0,0,15,15);
                g.FillRectangle(Brushes.White,4,5,7,6);
                using(Pen pen=new Pen(Color.White,1.3f)) { g.DrawArc(pen,8,5,5,5,270,180); g.DrawLine(pen,3,12,12,12); }
            }
            IntPtr handle=bitmap.GetHicon();
            try { using(Icon borrowed=Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
            finally { Native.DestroyIcon(handle); }
        }
    }
    internal TrayApp(bool start) : this(start,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlwaysAwake","settings.ini")) { }
    internal TrayApp(bool start,string configPath) {
        settingsPath=configPath; settings=AwakeSettings.Load(settingsPath); minutes=settings.Minutes;
        locked=Native.SessionLocked();
        IntPtr dispatcherHandle=dispatcher.Handle;
        stateItem.Enabled=false;
        menu.Items.Add(stateItem);
        menu.Items.Add(toggleItem);
        toggleItem.Click += delegate { Toggle(); };
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem duration = new ToolStripMenuItem("유지 시간");
        durationItems=new ToolStripMenuItem[choices.Length];
        for(int i=0;i<choices.Length;i++) {
            int chosen=choices[i];
            ToolStripMenuItem item=new ToolStripMenuItem(chosen==0 ? "종료할 때까지" : chosen+"분");
            item.Click += delegate { minutes=chosen; if(desired) deadline=chosen==0 ? long.MaxValue : clock.ElapsedMilliseconds+chosen*60000L; SaveSettings(); RefreshStatus(); };
            durationItems[i]=item; duration.DropDownItems.Add(item);
        }
        menu.Items.Add(duration);
        inputItem.Checked=settings.Input; inputItem.CheckOnClick=true;
        inputItem.CheckedChanged += delegate { nextPulse=clock.ElapsedMilliseconds+30000; SaveSettings(); RefreshStatus(); };
        displayItem.Checked=settings.Display; displayItem.CheckOnClick=true;
        displayItem.CheckedChanged += delegate { if(active && !Native.Power(true,displayItem.Checked)) Stop("전원 API 실패"); SaveSettings(); RefreshStatus(); };
        menu.Items.Add(inputItem); menu.Items.Add(displayItem);
        resumeItem.Checked=settings.Resume; resumeItem.CheckOnClick=true;
        resumeItem.CheckedChanged += delegate { SaveSettings(); };
        loginItem.CheckOnClick=true;
        using(RegistryKey key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) loginItem.Checked=key!=null && key.GetValue("AlwaysAwake")!=null;
        loginItem.CheckedChanged += delegate {
            try { using(RegistryKey key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) {
                if(loginItem.Checked) key.SetValue("AlwaysAwake","\""+Application.ExecutablePath+"\""); else key.DeleteValue("AlwaysAwake",false);
            } } catch(Exception ex) { MessageBox.Show("자동 실행 설정을 바꾸지 못했습니다.\n"+ex.Message,"Always Awake"); }
        };
        menu.Items.Add(resumeItem); menu.Items.Add(loginItem);
        menu.Items.Add(new ToolStripSeparator());
        string version=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        menu.Items.Add("업데이트 확인 · v"+version,null,delegate {
            try { Process.Start(new ProcessStartInfo("https://github.com/wnsgk0328-debug/always-awake/releases/latest") { UseShellExecute=true }); }
            catch(Exception) { MessageBox.Show("브라우저를 열 수 없습니다.\nhttps://github.com/wnsgk0328-debug/always-awake/releases/latest","Always Awake"); }
        });
        menu.Items.Add("사용 안내",null,delegate { MessageBox.Show("Always Awake\n\n왼쪽 클릭: 켜기 / 끄기\n오른쪽 클릭: 지속시간 및 자동 실행 설정\n기본 지속시간: 종료할 때까지\n설정과 켜기/끄기 선택은 다음 실행에도 기억합니다.\n잠금 중에는 중지하고, 잠금 해제 시 이전에 켜져 있었다면 재개합니다.\n직접 꺼 두면 잠금 해제나 재부팅 후에도 꺼져 있습니다.\n\nTeams 초록색 상태 유지는 보장하지 않습니다.\nF15 단축키가 있는 앱에서는 활동 입력을 끄세요.\n외부 통신 및 관리자 권한 없음.","Always Awake",MessageBoxButtons.OK,MessageBoxIcon.Information); });
        menu.Items.Add("종료",null,delegate { ExitThread(); });
        tray.ContextMenuStrip=menu;
        tray.MouseClick += delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) Toggle(); };
        tray.Visible=true;
        timer.Interval=1000; timer.Tick += Tick; timer.Start();
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        ready=true;
        if(start) Start();
        else {
            desired=settings.Enabled;
            deadline=minutes==0 ? long.MaxValue : clock.ElapsedMilliseconds+Math.Max(0,(settings.UntilUtc-DateTime.UtcNow.Ticks)/TimeSpan.TicksPerMillisecond);
            TryResume(); RefreshStatus();
        }
    }
    void SaveSettings() {
        if(!ready || disposed) return;
        settings.Enabled=desired; settings.Minutes=minutes;
        settings.Input=inputItem.Checked; settings.Display=displayItem.Checked; settings.Resume=resumeItem.Checked;
        settings.UntilUtc=minutes==0 || !desired ? 0 : DateTime.UtcNow.AddMilliseconds(Math.Max(0,deadline-clock.ElapsedMilliseconds)).Ticks;
        try { settings.Save(settingsPath); }
        catch(Exception) { if(!SelfTest) { tray.BalloonTipTitle="Always Awake"; tray.BalloonTipText="설정을 저장하지 못했습니다. 다음 실행에 반영되지 않을 수 있습니다."; tray.ShowBalloonTip(3000); } }
    }
    void TryResume() {
        if(!desired || locked || suspended || active) return;
        if(clock.ElapsedMilliseconds>=deadline) { Stop("설정한 시간이 끝났습니다."); return; }
        if(!Native.Power(true,displayItem.Checked)) { Stop("전원 API 실패"); return; }
        active=true; nextPulse=clock.ElapsedMilliseconds+30000; RefreshStatus();
    }
    void PauseForSession() {
        if(!resumeItem.Checked) { Stop(null); return; }
        active=false; Native.Power(false,false); RefreshStatus();
    }
    internal void Start() {
        desired=true;
        deadline=minutes==0 ? long.MaxValue : clock.ElapsedMilliseconds+minutes*60000L;
        TryResume(); SaveSettings(); RefreshStatus();
    }
    internal void Stop(string reason) {
        desired=false; active=false; Native.Power(false,false); SaveSettings(); RefreshStatus();
        if(!SelfTest && reason!=null) { tray.BalloonTipTitle="Always Awake 중지"; tray.BalloonTipText=reason; tray.ShowBalloonTip(2500); }
    }
    void Toggle() { if(desired) Stop(null); else Start(); }
    internal void Tick(object sender,EventArgs e) {
        long now=clock.ElapsedMilliseconds;
        if(desired && now>=deadline) { Stop("설정한 시간이 끝났습니다."); return; }
        if(!active) return;
        if(inputItem.Checked && now>=nextPulse) {
            nextPulse=now+30000;
            if(Native.IdleMilliseconds()>=5000) {
                if(!Native.Pulse()) { Stop("입력 요청이 차단되었습니다. 관리자 권한으로 재실행하지 말고 전원 유지 모드를 사용하세요."); return; }
                pulses++;
            }
        }
        RefreshStatus();
    }
    void SessionChanged(object sender,SessionSwitchEventArgs e) {
        if(disposed) return;
        if(dispatcher.InvokeRequired) { if(!disposed) dispatcher.BeginInvoke((MethodInvoker)delegate { SessionChanged(sender,e); }); return; }
        if(e.Reason==SessionSwitchReason.SessionLock || e.Reason==SessionSwitchReason.RemoteDisconnect || e.Reason==SessionSwitchReason.ConsoleDisconnect) { locked=true; PauseForSession(); }
        else if(e.Reason==SessionSwitchReason.SessionUnlock || e.Reason==SessionSwitchReason.ConsoleConnect || e.Reason==SessionSwitchReason.RemoteConnect) { locked=false; TryResume(); }
    }
    void PowerChanged(object sender,PowerModeChangedEventArgs e) {
        if(disposed) return;
        if(dispatcher.InvokeRequired) { if(!disposed) dispatcher.BeginInvoke((MethodInvoker)delegate { PowerChanged(sender,e); }); return; }
        if(e.Mode==PowerModes.Suspend) { suspended=true; PauseForSession(); }
        else if(e.Mode==PowerModes.Resume) { suspended=false; locked=Native.SessionLocked(); TryResume(); }
    }
    void RefreshStatus() {
        string remaining=active ? (deadline==long.MaxValue ? "시간 제한 없음" : Math.Max(0,(deadline-clock.ElapsedMilliseconds+59999)/60000)+"분 남음") : "클릭하여 시작";
        string state=active ? "켜짐" : desired ? "잠시 중지" : "꺼짐";
        tray.Icon=active ? onIcon : offIcon;
        tray.Text="Always Awake · "+state+" · "+remaining;
        stateItem.Text=state+" · "+remaining;
        toggleItem.Text=desired ? "끄기" : "켜기";
        for(int i=0;i<choices.Length;i++) durationItems[i].Checked=choices[i]==minutes;
    }
    internal void TestExpiration() { deadline=clock.ElapsedMilliseconds-1; Tick(null,EventArgs.Empty); }
    internal void TestLock() { SessionChanged(null,new SessionSwitchEventArgs(SessionSwitchReason.SessionLock)); }
    internal void UnlockForTest() { SessionChanged(null,new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock)); }
    internal void TestTimer() {
        SelfTest=true; inputItem.Checked=false; Start();
        timer.Interval=100;
        timer.Tick += delegate { TimerPass++; if(TimerPass>=3) { Stop(null); ExitThread(); } };
    }
    protected override void ExitThreadCore() { Cleanup(); base.ExitThreadCore(); }
    void Cleanup() {
        if(disposed) return; disposed=true;
        timer.Stop(); timer.Dispose(); active=false; Native.Power(false,false);
        SystemEvents.SessionSwitch-=SessionChanged; SystemEvents.PowerModeChanged-=PowerChanged;
        tray.Visible=false; tray.Dispose(); menu.Dispose(); dispatcher.Dispose(); onIcon.Dispose(); offIcon.Dispose();
    }
    protected override void Dispose(bool disposing) { if(disposing) Cleanup(); base.Dispose(disposing); }
}

static class Program {
    [STAThread] static int Main(string[] args) {
        bool owner;
        bool testRun=args.Length==2 && (args[0]=="--self-test" || args[0]=="--self-test-core");
        using(Mutex mutex=new Mutex(true,"Local\\AlwaysAwake_v1"+(testRun ? "_test_"+Guid.NewGuid().ToString("N") : ""),out owner)) {
            if(!owner) { MessageBox.Show("이미 실행 중입니다. 작업 표시줄의 숨겨진 아이콘(∧)을 확인하세요.","Always Awake"); return 2; }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==2 && (args[0]=="--self-test" || args[0]=="--self-test-core")) {
                string result="";
                try {
                    int size=Marshal.SizeOf(typeof(Native.INPUT));
                    if(size!=(IntPtr.Size==8 ? 40 : 28)) throw new Exception("INPUT layout mismatch");
                    string testSettings=Path.Combine(Path.GetTempPath(),"AlwaysAwake-selftest-"+Guid.NewGuid().ToString("N")+".ini");
                    using(TrayApp app=new TrayApp(false,testSettings)) {
                        app.SelfTest=true;
                        app.Start(); if(!app.Active) throw new Exception("Power enable failed");
                        app.Stop(null); if(app.Active) throw new Exception("Pause failed");
                        app.Start(); app.TestExpiration(); if(app.Active) throw new Exception("Expiration failed");
                        app.Start(); app.TestLock(); if(app.Active) throw new Exception("Lock stop failed");
                        app.Start(); if(app.Active) throw new Exception("Started while locked");
                        app.UnlockForTest(); if(!app.Active) throw new Exception("Unlock did not resume");
                        app.Stop(null); app.TestLock(); app.UnlockForTest(); if(app.Active) throw new Exception("Manual pause lost");
                        app.TestTimer(); Application.Run(app);
                        if(app.Active || app.TimerPass<3) throw new Exception("Timer loop failed");
                    }
                    if(args[0]=="--self-test-core") {
                        File.WriteAllText(args[1],"PASS: INPUT layout, tray initialization, power enable/release, pause, timed expiration, lock pause, unlock resume, manual pause preserved, real UI timer loop, cleanup.\r\nForeground input test not requested; no F15 input sent. Teams presence effect remains unverified.\r\n");
                        return 0;
                    }
                    bool received=false;
                    using(Form test=new Form()) {
                        test.Text="Always Awake 입력 검사"; test.Width=300; test.Height=100;
                        test.KeyPreview=true;
                        test.KeyUp += delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.F15) received=true; };
                        test.Show(); test.Activate(); Application.DoEvents();
                        if(Native.GetForegroundWindow()!=test.Handle) throw new Exception("Test window is not foreground; input test skipped safely");
                        if(!Native.Pulse()) throw new Exception("SendInput rejected");
                        for(int i=0;i<20 && !received;i++) { Application.DoEvents(); Thread.Sleep(10); }
                        test.Close();
                    }
                    if(!received) throw new Exception("F15 key-up not received by test window");
                    result="PASS: INPUT layout, tray initialization, power enable/release, pause, timed expiration, lock stop, unlock stays paused, real UI timer loop, cleanup, actual F15 key-up received by own test window.\r\nTeams presence effect remains unverified.\r\n";
                    File.WriteAllText(args[1],result); return 0;
                } catch(Exception ex) { Native.Power(false,false); File.WriteAllText(args[1],"FAIL: "+ex); return 1; }
            }
            using(TrayApp app=new TrayApp(args.Length==1 && args[0]=="--start")) Application.Run(app);
            return 0;
        }
    }
}
