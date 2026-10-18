# Rewind

A Windows instant-replay screen recorder for the SPOS (System Programming and Operating Systems) mini-project.

## Project status

The planned application is functionally complete. The user confirmed the core workflow works and accepted the saved-audio and border-free monitor fixes; that acceptance was recorded on 2026-10-18. Select a monitor or window, start buffering, and press **Save replay** or **Ctrl + Shift + F8** to export the preceding footage while capture continues. Replay durations are 15, 30 or 60 seconds, with optional system audio. Automated reliability checks, a 30-minute Windows Graphics Capture resource run and isolated portable-package checks passed. Physical-device switching/disconnection and other individually unreported hardware/presentation checks remain unverified; see [manual checks](docs/MANUAL-CHECKS.md).

The approved design is continuous screen and system-audio capture, bounded synchronized queues, an application-owned compressed replay ring buffer, and background MP4 export. The initial profile targets 30 seconds, 30 FPS and up to 1080p.

## Run locally

Requirements: Windows x64, Windows 10 build 19041 or later, PowerShell, and 7-Zip (`7z` on PATH) for first-time native dependency extraction.

From the project directory:

```powershell
# First-time setup: downloads the pinned SDK into .tools, without changing PATH.
.\scripts\setup.ps1

# First-time media setup: checksum-verified FFmpeg shared libraries into .tools.
.\scripts\setup-media.ps1

# Restore the locked dependencies and compile.
.\scripts\build.ps1

# Open the desktop app.
.\scripts\run.ps1

# Create a self-contained Windows x64 local preview (PowerShell 7).
.\scripts\package.ps1

# Check the resulting ZIP and exercise the packaged executable outside the checkout.
.\scripts\verify-package.ps1 -ArchivePath 'path printed by package.ps1'

# Verify startup and render an image into ignored artifacts/.
.\scripts\smoke-test.ps1

# Shortcut registration/conflicts, settings compatibility, tray commands and exit cleanup.
.\scripts\desktop-proof.ps1
.\scripts\desktop-proof.ps1 -Layout

# Failure handling and live commands; run only when those paths change.
.\scripts\reliability-proof.ps1
.\scripts\workflow-proof.ps1

# Sustained capture/resource measurements (30 minutes).
.\scripts\reliability-proof.ps1 -Minutes 30

# Focused checks for border-free monitor capture and saved-audio quality.
.\scripts\monitor-proof.ps1
.\scripts\audio-quality-proof.ps1

# Queue ownership, concurrent shutdown and audio timeline tests.
.\scripts\test.ps1

# Pre-trigger replay, eviction during export, repeated saves and stop/export checks.
.\scripts\replay-proof.ps1
.\scripts\replay-proof.ps1 -DefaultDuration
.\scripts\replay-proof.ps1 -VideoOnly

# Opt-in eight-second fixture: records only its own window and plays two quiet tones.
.\scripts\media-proof.ps1
.\scripts\media-proof.ps1 -VideoOnly
.\scripts\media-proof.ps1 -Silence
.\scripts\media-proof.ps1 -Cancel
```

The first setup and dependency restore require internet access. SDK files, native binaries, NuGet packages and CLI state stay in the ignored `.tools/` directory. `global.json` selects .NET SDK 10.0.402; `packages.lock.json` files record project dependencies; `native-dependencies.json` pins the FFmpeg archive and SHA-256. The Windows .NET targeting pack is selected by the pinned SDK and target framework.

For the local portable preview, extract the whole ZIP and open `Rewind.exe` inside `Rewind`. The .NET runtime and native media libraries are bundled. Keep the folder together. Settings still use the per-user location below. See [portable delivery](docs/DELIVERY.md), [package verification](docs/PACKAGE-VERIFICATION.md) and the [demonstration guide](docs/DEMO-GUIDE.md).

Application preferences are stored at `%LOCALAPPDATA%\Rewind\preferences.json`. The initial clip destination is the user's Videos folder under `Rewind`; changing the preference does not start recording or create a clip.

Choose the capture mode, replay duration, audio preference and save folder before starting. **Full monitor (no outline)** uses DXGI Desktop Duplication to capture the entire selected monitor without the yellow Windows capture border. This mode currently requires an unrotated landscape monitor and a local desktop supported by DXGI. **Window or display** opens the Windows source picker and retains the system capture border. Click **Start buffering** and leave it running. **Save replay** exports history ending at that button press; later footage cannot enter that clip. Capture continues during export. **Stop buffering** releases capture and clears the rolling history; an export that already has its snapshot can finish. Closing the window performs the same cleanup before exiting.

System audio uses stereo 48 kHz AAC at 192 kbps with 6 dB recording headroom. Saved clips are therefore quieter than the live output; Windows output volume is unchanged. Device sample positions distinguish timestamp jitter from genuine missing audio, and gradual timing correction follows clock drift. A 100 ms WASAPI buffer and Windows audio thread priority reduce capture overruns. See [audio and monitor verification](docs/QUALITY-VERIFICATION.md) for the measured scope and focused manual recheck.

The global shortcut defaults to **Ctrl + Shift + F8**; F9 and F10 alternatives are available in the shortcut picker. Changes take effect immediately; **Save preferences** retains the selection for the next launch. If another application owns a shortcut, Rewind explains the conflict and keeps its previous working shortcut. Minimize the window to keep Rewind in the system tray. Double-click the tray icon to restore it, or right-click for Open, Start/Stop, Save replay, Open clips folder and Exit. Starting from the tray opens the window for source selection. Save and stop commands share the window's busy guards. Hidden-window saves and errors request Windows tray notifications, subject to Windows notification settings. Closing the window or choosing Exit ends the application; minimizing keeps it running.

If the buffer has not filled yet, Save exports the available history. Clips can start up to roughly one keyframe interval earlier than the selected duration; the app reports the actual duration. The compressed packet ring is limited to 128 MiB and 8,192 slots. Under byte pressure, whole video groups are evicted, which can shorten available history. One protected export snapshot is allowed at a time, holding at most another ring's worth of packet data. Raw capture queues and native encoder memory are additional, separately bounded resources. System audio captures the default output device's full mix.

Closing/resizing the selected source, changing the default multimedia playback device or losing that audio device ends the current session with a restart message. Start buffering again after the source/device change. Saved clips remain on disk; unsaved rolling history is cleared when the stopped session is cleaned up.

## Layout

```text
src/
  Rewind.App/       WPF replay controls, source picker, settings and diagnostic fixtures
  Rewind.Core/      Bounded queue, circular packet ring and protected snapshot lease
  Rewind.Media/     Capture, audio timing, live encoding and independent MP4 export
tests/             Queue concurrency, audio timeline, ring budgets and snapshot tests
scripts/           Project-local SDK setup, build, run and startup verification
docs/              Build plan and current implementation decisions
```

## Development workflow

The project was developed and verified locally before source delivery to [AltamashM7/spos-replayer](https://github.com/AltamashM7/spos-replayer), authorized by the user on 2026-10-18. The repository contains source, tests, build scripts, locked dependency metadata and documentation. SDK caches, native DLLs, recordings, user settings and portable ZIPs are excluded. Routine local changes do not require a push after every response.

The repository's own license has not been selected. Dependency licenses remain applicable to their respective components. The local FFmpeg build identifies itself as GPLv3; publishing a portable executable is a separate delivery step requiring the appropriate source/license materials. See [third-party dependencies and licensing](docs/THIRD-PARTY.md).

See [the local build plan](docs/BUILD-PLAN.md) for the remaining milestones and SPOS requirements.

See [media verification](docs/MEDIA-VERIFICATION.md) for the measured proof and current scope, and [third-party dependencies](docs/THIRD-PARTY.md) for the pinned libraries.

See [replay verification](docs/REPLAY-VERIFICATION.md) for the pre-trigger and 30-second-profile checks.

See [desktop verification](docs/DESKTOP-VERIFICATION.md) for shortcut and tray checks.

See [reliability verification](docs/RELIABILITY-VERIFICATION.md) for failure/resource checks, [manual checks](docs/MANUAL-CHECKS.md) for remaining hardware and keyboard verification, and [architecture](docs/ARCHITECTURE.md) for the SPOS concepts and thread/resource ownership.
