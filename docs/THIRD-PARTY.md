# Media dependencies

| Dependency | Version | Role | Upstream license |
|---|---|---|---|
| FFmpeg.AutoGen | 9.0.1.1 | C# bindings for the codec/container libraries | MIT |
| FFmpeg shared Windows build from Gyan Doshi | 9.0.2 | H.264/AAC encoding, colour conversion and MP4 muxing | GPLv3 build; bundled license retained in `.tools/ffmpeg/ffmpeg-9.0.2-full_build-shared/LICENSE` |
| NAudio.Wasapi | 3.1.0 | WASAPI output-device capture and diagnostic playback | MIT |
| Vortice.Direct3D11 | 3.8.3 | Direct3D11/DXGI device, texture and staging-buffer access | MIT |

NuGet transitive versions and content hashes are in the checked-in package lock files. Native archive URL and SHA-256 are in `native-dependencies.json`. Native binaries, their accompanying documentation and licenses stay in the ignored `.tools/` tree during local development.

Upstream references:

- [FFmpeg download page](https://ffmpeg.org/download.html), which links the Windows build provider.
- [Pinned Windows build](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2).
- [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen).
- [NAudio](https://github.com/naudio/NAudio).
- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows).

The local portable preview copies only avcodec, avformat, avutil, swscale and swresample DLLs into `native/ffmpeg`. It retains the original FFmpeg license and build README, original application NuGet packages and metadata, and runtime licenses/notices under `notices`. The source snapshot contains Rewind's project source; it is not a complete corresponding-source deliverable for all native build components.

## What third-party licensing means here

These libraries were written by other projects. Their licenses specify how copies may be used, modified and redistributed. Attribution and source obligations are separate from whether a dependency costs money.

The principal managed libraries above use MIT terms: copies of the libraries must retain the relevant copyright/license notices. The local portable packager already retains original NuGet packages/metadata and .NET runtime notices; transitive dependency licenses must be considered as well. A license on Rewind would not replace any upstream license.

The pinned Gyan FFmpeg build explicitly identifies itself as **GPLv3**, and the app uses its `libx264` encoder. Distributing an application linked with that GPL build requires GPL-compatible terms for the combined work and the required corresponding-source/license materials. Dynamic DLL loading does not by itself remove those requirements. The Rewind source snapshot alone is not the corresponding source for all components compiled into this FFmpeg build. See [FFmpeg's explanation](https://ffmpeg.org/legal.html) and [GPLv3](https://www.gnu.org/licenses/gpl-3.0.html), especially sections 5 and 6.

Original GPL text and the pinned build's source/version/configuration record are retained in [notices/ffmpeg](notices/ffmpeg). These are notices and provenance, not a claim that the complete corresponding-source deliverable is assembled.

## Scope of this repository delivery

The user authorized pushing the finished C# source, tests, scripts and documentation to the existing private `AltamashM7/spos-replayer` repository on 2026-10-18. Native libraries and portable ZIPs remain excluded. Setup scripts download dependencies separately; no upstream binary is distributed in this commit.

The project's own license has not been chosen, and this source delivery does not assign one on the user's behalf or describe the repository as a licensed open-source release. Existing copyright and GitHub hosting terms still apply; see GitHub's [explanation of repositories without a license](https://choosealicense.com/no-permission/). Before distributing the portable executable, select compatible project terms and prepare the required corresponding-source and dependency notices. See [delivery](DELIVERY.md).
