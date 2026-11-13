# Always Awake

A tiny, portable Windows tray utility to keep your PC awake. Built using Windows' existing .NET Framework 4.x; no runtime is bundled.

## Download

[Download the latest portable ZIP](https://github.com/wnsgk0328-debug/always-awake/releases/latest/download/AlwaysAwake-win-portable.zip) · [All releases](https://github.com/wnsgk0328-debug/always-awake/releases)

Extract the ZIP and run **AlwaysAwake.exe**. No installation or administrator rights required. First launch is paused; later launches restore the saved enabled/paused choice.

## Use / 사용법

- Left-click the coffee cup tray icon to start or pause. Green = active; gray = paused. The icon may be under the taskbar's hidden icons arrow.
- Right-click to select 10, 15, 25, 30, 60 or 120 minutes, or until exit. The default is until exit. Duration, input/display options and enabled/paused choice are saved per user.
- **30초마다 활동 입력 (F15)**: send one F15 key-up every 30 seconds, skipping cycles if input occurred in the previous five seconds.
- **화면도 켜 두기**: also request that the display stay on. System sleep prevention remains active while the application is enabled.
- **업데이트 확인** opens the latest GitHub Release in your browser, only when clicked. Exit the app, download the new release, verify SHA256SUMS.txt if desired, and replace the old EXE. No background downloader or silent executable replacement.
- `AlwaysAwake.exe --start` starts immediately instead of paused.

The app pauses on lock, session disconnect or suspend notifications and resumes only after unlock/reconnect/resume if it was previously enabled. Manually paused sessions stay paused. An expired finite timer is not restarted. Toggle **잠금 해제 후 자동 재개** to control this behavior.

Enable **Windows 로그인 시 자동 실행** to launch after signing into Windows, including after reboot. This creates a current-user Run entry pointing at the current EXE; keep that file in place. Exiting the app ends the current process but keeps its saved choice for the next launch. Use Pause if you want it to stay off next time. The app never interacts with the lock screen or enters a password.

## Privacy and limitations

- No telemetry, network client, key logging, document access or administrator elevation. Settings are stored at `%LOCALAPPDATA%\AlwaysAwake\settings.ini`. Optional startup registration uses only the current user's Run key. Update checking opens a fixed GitHub page in the default browser.
- F15 may conflict with application shortcuts. Disable activity input when that matters and use power requests alone.
- **Teams Available/green status is not guaranteed.** The app is not a Teams integration. Presence depends on Teams and server-side conditions too.
- The EXE is not code-signed. Respect Windows/organization security controls; this app provides no bypass.
- Small EXE size excludes the .NET Framework already installed on Windows. Runtime memory is larger than the file size.

## Build and release

Windows with .NET Framework 4.x. No package restore or external dependencies.

```powershell
.\Build.ps1 -Test
.\Package.ps1 -Test
```

The build test covers tray initialization, power request enable/release, pause, timed expiration, lock/unlock handling and the WinForms timer. `Regression.cs` additionally tests persistence, manual pause, saved duration and expiry while locked using isolated temporary settings. Tests send no synthetic input and do not lock/reboot the real PC. For a separate interactive F15 test, run `AlwaysAwake.exe --self-test report.txt`; it briefly shows its own window and refuses to send input unless that window is foreground.

To publish an update: change `version.txt`, update `CHANGELOG.md`, commit, and push a matching `vX.Y.Z` tag. GitHub Actions builds the release on Windows and publishes the EXE, portable ZIP and SHA-256 hashes. Each version has a new release; prior releases remain available. Keep the ZIP filename unchanged so the download link and update menu continue to work.
