# Changelog

## 1.1.0 — 2026-11-13

- Remember duration, input/display preferences and enabled/paused choice across launches.
- Default duration is until exit; optional current-user Windows login startup.
- Separate intended enabled state from temporary lock/suspend pause; resume on unlock only if previously enabled.
- Preserve manual pause and finite-session expiry; never interact with the lock screen.
- Isolate self-test settings and mutex so tests do not interrupt a running app.

## 1.0.0 — 2026-10-22

- First Always Awake release: small Windows tray application with coffee cup icon.
- Time-limited power/display keep-awake requests and optional 30-second F15 activity input.
- Pause on lock, disconnect and suspend notifications; no automatic resume on unlock.
- Added version information, a user-initiated update link, release packaging and SHA-256 checksums.
