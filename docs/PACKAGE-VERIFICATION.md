# Portable package verification — 2026-10-16

The local Windows x64 self-contained Release package passed deployment checks. It includes .NET 10.0.13, five FFmpeg DLLs from the pinned 9.0.2 shared build, documentation, original notices/packages, a Rewind source snapshot, provenance metadata and SHA-256 inventory.

## Verified boundary

- 528 package-file hashes passed in the initial verified bundle.
- The verifier copied the bundle to a new temporary directory outside the checkout. The child process had no SDK paths or DOTNET_ROOT overrides available and exercised a blank FFmpeg override.
- The real WPF main window rendered and reported capture support.
- `Rewind.exe` loaded its core runtime from its own copied directory. Loaded FFmpeg modules came from that bundle's `native/ffmpeg` directory.
- The packaged app captured only its own animated fixture window with system audio, exported a 3.2-second replay containing 96 H.264 frames and AAC audio, and shut down.
- Video and audio track durations both measured 3.200000 seconds. FFmpeg fully decoded the exported clip without an error.
- No external FFmpeg executables are needed by the app. Development verification tools inspect/decode the result separately.
- A normal solution restore still passed in locked mode after adding Windows deployment assets. The existing queue/ring, media-content, idle desktop and 30-minute suites were not rerun.

An initial isolated probe exposed the distinction between an absent and an empty environment override. The loader now treats blank/whitespace `REWIND_FFMPEG_PATH` as unset and discovers the app-local bundle. The corrected probe passed with that empty override. An explicit nonempty override still takes precedence; the development checkout fallback remains available.

Evidence: `artifacts/package-proof-492a4045f43246d380197bfe9570e3eb/verification.json`, `startup/startup.png`, `media/package-proof.json`, `media/probe.json` and `media/package-replay.mp4`. The verified binary bundle was `artifacts/packages/Rewind-win-x64-preview-eb49f558/Rewind`.

The final documentation repackage uses those same binaries. Its archive hash, extracted-file inventory and binary comparison are checked separately, without repeating capture/encoding tests. Use the current ZIP printed by `scripts/package.ps1`; package identities change on each assembly.

## Scope

The accepted updated build is `Rewind-win-x64-preview-e0fc614c.zip`, SHA-256 `5D0D47CA38A99EC456952F577E5C06674F6050E74095A9A2704EAF3FC85350EF`. It passed 530 file hashes and isolated .NET 10.0.13/FFmpeg 9.0.2 startup and capture/export checks. Its 91-frame WGC replay has matching 3.033333-second audio/video tracks and fully decodes. The isolated packaged monitor fixture additionally passed ten lifecycle checks and fully decoded both audio-enabled and video-only exports. Evidence: `artifacts/package-proof-c462a37ed20042d6bd7838dd6947c51d/verification.json` and `monitor/verification.json`.

The user confirmed the updated app works; acceptance was recorded on 2026-10-18. This documentation update does not change the accepted binary ZIP or rerun previously passing checks. The repository source delivery excludes executable packages.

This is a local preview on the development PC with SDK paths removed, not a fresh Windows installation test. User acceptance closes the reported audio and outline issues. Physical audio-device changes and other individually unreported conditions remain unverified in `MANUAL-CHECKS.md`. Public binary/source-notice preparation and the repository-license decision are also pending, as described in `DELIVERY.md`.
