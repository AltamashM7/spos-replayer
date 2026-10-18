# Desktop workflow — 2026-10-15

## Implemented behavior

Rewind registers Ctrl+Shift+F8 through Windows RegisterHotKey with MOD_NOREPEAT. The picker offers F9 and F10 alternatives. Replacement acquires the new registration before releasing the old one, so a conflict preserves the previous shortcut. The UI explains the conflict and prevents persisting a failed selection. Older preference files receive F8 without losing their duration, audio or folder values.

Minimize hides the main window while retaining a NotifyIcon tray control. Double-click or Open restores the window. The menu provides Start/Stop, Save replay, Open clips folder and Exit. Starting restores the window for the capture picker. Menu availability mirrors recording/export state. The hotkey, button and tray Save commands use the same guarded asynchronous save method. Hidden-window save results and capture failures request a tray notification. Windows may suppress those notifications according to the user's settings.

Close and Exit use the existing recording/export cleanup path. After the window closes, the tray icon is disposed and the global registration is released. If tray initialization fails, minimizing leaves the normal taskbar window available.

## Focused verification

Build passed with zero warnings and errors. Run `scripts/desktop-proof.ps1` for the opt-in integration fixture, or add `-SkipBuild` when the current code is already built. It uses a new ignored artifact directory for settings and reports; it neither captures media nor injects user keystrokes.

Nineteen checks passed:

- Legacy settings migration and rejection of unsupported shortcut values.
- Initial shortcut/tray creation, real OS registration conflicts and a competing reservation.
- Failed replacement preserving the old shortcut, blocking invalid persistence, and successful replacement freeing the old shortcut.
- Shortcut persistence/reload, ignoring stale WM_HOTKEY IDs and routing the active ID through the idle save guard.
- Rendered controls fitting the window, minimizing to the tray, and restoring through the actual Open menu item.
- Actual tray menu item dispatch/availability in idle, recording and busy states using controlled state inputs.
- Actual tray Exit closing the idle main window, hiding/disposal of the icon, unregistering the shortcut, and another window successfully registering the released combination.

The fixture exercises the native registration and WPF message hook with explicit WM_HOTKEY messages. Physical shortcut input, Windows notification presentation and commands during a live capture session were not rechecked in this pass. The underlying capture, codec, replay and stop/export behavior was unchanged; the previously passing engine tests and native media fixtures were not rerun.

Evidence from this run: `artifacts/desktop-c2a2a74eda364a96bef665a81701c4ab/desktop.json` and `desktop.png`. The screenshot was visually reviewed and has no clipped controls. Artifact directories are ignored by source control.

## Remaining work

Exercise full desktop controls during sustained capture, overload, source closure/resizing, output-device changes, additional filesystem failures and a 30–60-minute resource run. Packaging and demonstration documentation follow those reliability checks.

The subsequent [reliability pass](RELIABILITY-VERIFICATION.md) adds live command, failure-path and sustained checks. Physical keyboard input and hardware changes remain listed in [manual checks](MANUAL-CHECKS.md).
