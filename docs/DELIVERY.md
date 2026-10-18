# Source and local portable delivery

## Accepted application and repository delivery

The application is functionally complete. The user confirmed normal operation and the audio/outline fixes, with acceptance recorded on 2026-10-18. The accepted local package is `Rewind-win-x64-preview-e0fc614c.zip` (SHA-256 `5D0D47CA38A99EC456952F577E5C06674F6050E74095A9A2704EAF3FC85350EF`). Its runtime, capture/export and file-integrity checks passed before acceptance.

On 2026-10-18 the user authorized updating the documentation and pushing the finished source to [AltamashM7/spos-replayer](https://github.com/AltamashM7/spos-replayer). Delivery includes source, tests, scripts, dependency locks and documentation. The repository's existing private visibility is preserved. Native DLLs, SDK/NuGet caches, recordings, user preferences and portable ZIPs remain excluded by `.gitignore`. No GitHub binary release is part of this source push.

The existing accepted ZIP remains an immutable local snapshot; its embedded documentation predates the acceptance record. Current repository documentation records acceptance without rebuilding or retesting unchanged application binaries.

## Build and use

```powershell
.\scripts\package.ps1
# The script prints a unique package folder, ZIP path and SHA-256.

.\scripts\verify-package.ps1 -ArchivePath 'the printed ZIP path'
```

Extract the complete ZIP and open `Rewind.exe` inside `Rewind`. The package includes the Windows x64 .NET runtime and the required FFmpeg shared libraries. It does not need the checkout's `.tools` directory or a separately installed SDK/runtime. The build is a folder deployment, with trimming and single-file publishing disabled for WPF/WinRT/native interoperability. See Microsoft's [deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/) for the self-contained deployment model.

Packaging scripts require PowerShell 7. The output is under ignored `artifacts/packages`. Each invocation uses a fresh directory; it does not delete older packages. `-SkipPublish` can reassemble documentation/notices without rebuilding only when the production-source fingerprint still matches the published binary inputs. A ZIP contains:

- `Rewind.exe`, managed dependencies and the bundled .NET runtime.
- `native/ffmpeg` with the codec/container/scaling/resampling DLLs.
- `README.txt`, project documentation and a source snapshot ZIP.
- `notices` with original FFmpeg notices/build configuration, runtime notices and original application NuGet packages plus their metadata index.
- `package-manifest.json` recording versions, runtime identity, source/native provenance and pending manual/release status.
- `SHA256SUMS.txt` for package-file integrity. The outer ZIP also receives a SHA-256 sidecar.

The source snapshot excludes `.git`, SDK caches, binaries, recording artifacts and user settings. It is the Rewind project source, not a complete corresponding-source collection for FFmpeg and every library compiled into that upstream build.

## Verification scope

The package verifier validates the outer ZIP hash, extracts it and checks the complete file inventory/hashes. It copies the package into a fresh temporary directory outside the checkout and launches `Rewind.exe` there. It removes `DOTNET_ROOT` and developer paths from the child environment; the capture probe also exercises a deliberately blank FFmpeg override. It checks actual WPF startup, then captures only its own animated window for approximately three seconds and saves a replay with system audio. The report must show an app-local .NET runtime and native FFmpeg modules from the copied bundle. The generated MP4 must contain H.264/AAC and fully decode.

Use `-PackageDirectory` to verify an already-extracted folder. `-IntegrityOnly` checks archive/files/runtime metadata without launching or recording; it is suitable for a documentation-only repackage whose binaries have already been verified and compared unchanged.

This validates the deployment boundary without rerunning the already-verified engine, ring, desktop or 30-minute suites. It is not a fresh Windows installation test or a substitute for the physical-device/keyboard checks in `MANUAL-CHECKS.md`.

## Remaining public binary release work

The local app has user acceptance. Physical playback-device changes and individually unreported keyboard/presentation conditions remain unverified in `MANUAL-CHECKS.md`; they have not been silently marked passed. The source delivery does not upload an executable or create a GitHub Release.

The pinned upstream FFmpeg build identifies itself as GPLv3 in its included README/license. Those original notices are retained. Public binary publication still needs the corresponding third-party source/notice deliverables and the project's repository-license decision; a source URL or NuGet metadata index alone is not marked as completing that work. See [FFmpeg's license information](https://ffmpeg.org/legal.html) and the bundled build license. No repository license has been chosen on the user's behalf. The local source snapshot and provenance manifest support that later preparation.

The user's authorization permits this source delivery to `AltamashM7/spos-replayer`. Any later public executable release is a separate step. Routine development and local packaging do not require GitHub interaction after every change.
