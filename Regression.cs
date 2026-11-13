using System;
using System.IO;
using System.Reflection;
class Regression {
    static Type type; static int failures;
    static BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object New(string path) {
        ConstructorInfo ctor=type.GetConstructor(flags,null,new Type[]{typeof(bool),typeof(string)},null);
        object app=ctor!=null ? ctor.Invoke(new object[]{false,path}) : Activator.CreateInstance(type,flags,null,new object[]{false},null);
        type.GetField("SelfTest",flags).SetValue(app,true); return app;
    }
    static void Call(object app,string name,params object[] args) { type.GetMethod(name,flags).Invoke(app,args); }
    static bool Active(object app) { return (bool)type.GetProperty("Active",flags).GetValue(app,null); }
    static void Check(bool ok,string message) { Console.WriteLine((ok?"PASS ":"FAIL ")+message); if(!ok) failures++; }
    [STAThread] static int Main(string[] args) {
        type=Assembly.LoadFrom(args[0]).GetType("TrayApp");
        string temp=Path.Combine(Path.GetTempPath(),"AlwaysAwake-regression-"+Guid.NewGuid().ToString("N")+".ini");
        object app=New(temp);
        try {
            Check((int)type.GetField("minutes",flags).GetValue(app)==0,"Default duration is until exit");
            Call(app,"Start"); Call(app,"TestLock"); Check(!Active(app),"Lock pauses real activity");
            Call(app,"UnlockForTest"); Check(Active(app),"Unlock resumes previously enabled activity");
            Call(app,"Stop",new object[]{null}); Call(app,"TestLock"); Call(app,"UnlockForTest"); Check(!Active(app),"Manual pause survives lock/unlock");
            Call(app,"Start");
        } finally { ((IDisposable)app).Dispose(); }
        app=New(temp);
        try { Check(Active(app),"Enabled choice survives process restart"); }
        finally { Call(app,"Stop",new object[]{null}); ((IDisposable)app).Dispose(); }
        app=New(temp);
        try { Check(!Active(app),"Manual pause survives process restart"); }
        finally { ((IDisposable)app).Dispose(); }
        app=New(temp);
        try {
            Array durations=(Array)type.GetField("durationItems",flags).GetValue(app);
            object fifteen=durations.GetValue(1); fifteen.GetType().GetMethod("PerformClick").Invoke(fifteen,null);
        } finally { ((IDisposable)app).Dispose(); }
        app=New(temp);
        try {
            Check((int)type.GetField("minutes",flags).GetValue(app)==15,"Selected duration survives process restart");
            Call(app,"Start"); Call(app,"TestLock"); Call(app,"TestExpiration"); Call(app,"UnlockForTest");
            Check(!Active(app),"Finite session expiring while locked does not restart");
        } finally { ((IDisposable)app).Dispose(); }
        Console.WriteLine("Failures: "+failures); return failures==0?0:1;
    }
}
