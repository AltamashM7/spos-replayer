REWIND — WINDOWS X64 LOCAL PREVIEW

Extract the entire ZIP, then open Rewind.exe inside the Rewind folder.
Keep all DLLs, native files and other folders together. No SDK, .NET installation,
FFmpeg installation, administrator rights or installer is needed to launch it.
Requires Windows x64, Windows 10 build 19041 or later, and a supported capture source.

Choose a replay length, audio preference and save folder. For border-free recording,
select Full monitor (no outline), choose your monitor, then click Start buffering.
This records the entire monitor and currently requires an unrotated landscape display.
Window or display uses the Windows picker and retains the Windows capture border.
Save replay (or Ctrl+Shift+F8)
keeps footage from before the trigger. A buffer that has not filled yet saves
the available history. Saving does not stop capture.

Minimize to the system tray. Double-click the icon to restore the window, or
right-click it for Start/Stop, Save replay, Open clips folder and Exit.
Closing the window exits. Stop/Exit clears unsaved history while allowing an
already-started export to finish. Saved clips remain on disk.

The initial save folder is Videos\Rewind. Preferences are saved under
%LOCALAPPDATA%\Rewind\preferences.json when you click Save preferences.
This portable build uses those same per-user settings as the development build.

Audio records the default multimedia output device's full mix. A source resize,
source closure or playback-device change stops that session with a restart message.
Start buffering again after the change. Microphone capture is not included.
Saved audio uses 192 kbps AAC and 6 dB headroom, so it is quieter than live playback.
Windows output volume is unchanged. Display/desktop changes may require restarting
a monitor-capture session. See docs\QUALITY-VERIFICATION.md for the focused fixes.

This is a local preview. The user accepted normal operation and the audio/outline
fixes; acceptance was recorded on 2026-10-18. Automated reliability and the earlier
30-minute Windows Graphics Capture resource checks passed. Physical device changes
and individually unreported conditions remain unverified in docs\MANUAL-CHECKS.md.
Use docs\DEMO-GUIDE.md
for the project demonstration and docs\ARCHITECTURE.md for the SPOS concepts.

Source snapshot: source\Rewind-source.zip
Package provenance: package-manifest.json
File integrity: SHA256SUMS.txt
Upstream notices and original NuGet packages: notices\

This folder is prepared for local testing. Public binary publication and repository
licensing/source-notice preparation are still pending. The included GPLv3 FFmpeg
license and build README are retained in notices\ffmpeg; source links alone are
not presented as a completed third-party redistribution/source deliverable.
