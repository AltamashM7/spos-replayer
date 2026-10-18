# Saved audio and border-free monitor capture — 2026-10-16

The user confirmed the basic recorder workflow worked, but reported occasional chipped audio in saved clips at Windows output volume 80–90 and a yellow capture outline. They approved full-monitor capture without an outline. The original clip in `Videos/Rewind` was inspected without modifying it.

## Changes

- **Full monitor (no outline)** uses DXGI Desktop Duplication on the selected monitor's graphics adapter. It creates no Windows Graphics Capture session. Window/display picker mode remains available with its system border. Capture mode and monitor selection persist in preferences.
- DXGI capture has one worker owning acquisition and D3D context access, bounded frame storage, hardware-pointer composition, a bounded acquire timeout and explicit display-change failure/cleanup. Pointer-only notifications cannot initialize the first desktop frame. This first version requires an unrotated landscape monitor and supports approximately 4K input before the normal 1080p output scaling.
- Audio packets carry device positions and discontinuity metadata. The timeline reader preserves continuous device samples despite small QPC timestamp jitter, follows filtered clock drift through gradual interpolated corrections, and preserves real silence/missing intervals. It holds at most two packets outside the bounded queue.
- WASAPI uses a 100 ms capture buffer and MMCSS Audio thread priority instead of the former 40 ms unprioritized capture. Stereo AAC is now 192 kbps with fixed 0.5 recording gain (approximately 6 dB headroom), finite-value sanitization and an input safety clamp. Windows output volume is unchanged.

The user's original decoded clip had peaks up to +2.74 dBFS. The original controlled recording showed continuous device positions but QPC jitter of approximately ±1–9 samples; the old timeline inserted zeros or skipped samples at those boundaries. These observations support the changes, but do not establish that every audible artifact had one cause. An intermediate recording also exposed genuine buffer overruns, which timing correction cannot reconstruct.

Microsoft describes the [Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api) and the newer, consent-dependent WGC [border setting](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired). The local PC runs Windows 10 build 19045; the newer WGC border setting is unavailable there. The pinned NAudio recorder supports [capture buffering and MMCSS priority](https://naudio.github.io/NAudio/api/NAudio.Wave.WasapiRecorderBuilder.html).

## Focused checks

The following checks exercise changed paths. The unchanged ring, queue, endpoint-policy, shortcut/tray and 30-minute resource suites were not repeated.

- Fourteen audio timeline/quality tests pass: timestamp jitter, exact continuous waveform preservation, sustained clock drift, partial reads, real gaps/discontinuities, silence placement and headroom/invalid samples.
- Four pointer tests pass: color alpha, masked-color XOR, monochrome AND/XOR, clipping and malformed shape bounds.
- The monitor fixture starts two sessions through the production UI command, persists mode/monitor using isolated settings, locks capture preferences, exports while capture continues, then stops and restarts. One clip contains H.264/AAC; the other is video-only. Both fully decode at 1366×768. Sampled red/blue frames have correct outer-edge colors (99th-percentile error 1/255) and no yellow edge pixels. Separate hardware cursor composition is visible in the controlled capture.
- The updated layout fits the 1366×728 work area without scrolling and keeps footer controls reachable in a compact 540-pixel window through scrolling.
- The eight-second screen-flash/audio-pulse fixture fully decodes with both tones detected. Measured audio-minus-video offsets are −90 ms and −56.7 ms, within the existing 150 ms tolerance. No audio queue packets were dropped. Evidence: `artifacts/media-proof-e62fb9d90cd3443db80cb294c8d22bda/verification.json`.
- The final 12-second quiet-tone run has 1,307 observed packets and no missing device samples across 1,306 boundaries. One initial discontinuity flag is expected at stream startup. The timing correction adjusts 704 packets.
- Comparing steady 10 ms blocks of decoded 730 Hz audio, the 95th-percentile normalized tone residual falls from 0.07043 to 0.00444. Blocks above 20% residual fall from six to zero. This is a controlled waveform measurement, not a music-listening score.
- An offline production-encoder test supplies a sine with amplitude 1.4, above PCM full scale, without playing it through the speakers. Its decoded AAC peak remains below 0 dBFS (−0.033 dBFS, including the startup transient). Headroom reduces overload risk; it is not a true-peak limiter or restoration of audio already clipped upstream.

Evidence:

- Baseline audio: `artifacts/audio-quality-8b80812e5fc8467299e7f2bba6416a51`.
- Final audio and combined decoded verification: `artifacts/audio-quality-ad5a16370cac4a9c8cd8110ad6fb908c/quality-verification.json`.
- Monitor lifecycle and clips: `artifacts/monitor-proof-473af50a588f4df79b6721f6a6d947ba`.
- Layout: `artifacts/desktop-05b574c5d49543bbb2f581aa0c0ba9c2`.

One intermediate 100 ms capture run still encountered a 540 ms device gap; the subsequent final run had none. The checks do not guarantee lossless capture under arbitrary machine stalls, driver faults or heavy load. No new 30-minute performance claim is made for the DXGI backend.

## Repeat only when these paths change

```powershell
.\scripts\monitor-proof.ps1
.\scripts\audio-quality-proof.ps1
# With Python + NumPy available, compare the directories printed above:
python scripts/analyze-quality.py --baseline <baseline-directory> --audio <new-audio-directory> --monitor <new-monitor-directory>
```

The monitor fixture temporarily covers its selected display with its own opaque test surface. The audio fixture records its own animated window and plays a quiet generated tone; the loud-input encoder test is entirely offline. Neither changes Windows volume or writes real user preferences. Diagnostic clips remain under ignored `artifacts`.

The user confirmed the updated build works; acceptance of the reported audio and outline fixes was recorded on 2026-10-18 in [MANUAL-CHECKS.md](MANUAL-CHECKS.md). Saved audio is intentionally quieter. Rotated displays, multiple adapters, protected content, remote/secure desktops and long-running DXGI performance have not been certified by these focused checks. The user subsequently authorized source delivery to `AltamashM7/spos-replayer`; portable binaries remain local.
