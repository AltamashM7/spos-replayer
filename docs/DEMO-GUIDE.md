# Rewind demonstration guide

## Prepare

Extract the complete portable ZIP and open `Rewind.exe`, or use `scripts/run.ps1` from the checkout. Choose a stable, unprotected window or monitor; keep its geometry and playback device unchanged during the demonstration. Check the save folder and shortcut availability. Close other copies of Rewind to avoid a global shortcut conflict.

Complete [manual checks](MANUAL-CHECKS.md) before presenting. In particular, confirm an audible sample and the shortcut from another application. This guide describes the intended demonstration; it does not record those pending checks as passed.

## Five-minute demonstration

1. **Explain the problem.** An ordinary recorder starts after the event. Rewind continuously keeps a bounded amount of recent footage and saves it when requested.
2. **Start a session.** Select 30 seconds with system audio. Choose **Full monitor (no outline)** and your monitor, then click Start buffering; this records the whole monitor. Alternatively choose **Window or display** and select the source in the Windows picker, which retains the system border. Point out available history and the compressed-buffer statistics.
3. **Show a pre-trigger event.** Make a visible change in the selected application and play a short sound. Wait several seconds before pressing Save replay. Open the saved clip and show that the earlier event is present. Use the actual reported clip duration; keyframe alignment can make it slightly longer than 30 seconds, and early saves contain partial history.
4. **Show concurrency.** Save another clip while continuing visible activity in the source. Capture remains active during export. Explain that the export holds an immutable packet snapshot while the live circular buffer keeps advancing.
5. **Show the desktop workflow.** Minimize Rewind, focus another application and use the configured shortcut. Restore it through the tray. Avoid changing resolution, resizing the selected source or switching audio devices mid-demonstration unless demonstrating the explicit stop/restart policy.
6. **Show resource evidence and cleanup.** Open the measured results in `RELIABILITY-VERIFICATION.md`. Stop buffering or choose Exit. Explain that unsaved history is released and any snapshot export already underway can finish.

## Explain the SPOS work

| Concept | Implementation to show |
|---|---|
| Producer/consumer coordination | Screen/audio callbacks produce owned inputs; an encoder worker consumes them |
| Mutual exclusion | Queue and ring locks protect short shared-state operations |
| Bounded buffering | 12 video inputs, 64 audio packets, 8,192 compressed slots and a 128 MiB accounted ring budget |
| Circular storage | Keyframe-aware eviction replaces older compressed history |
| Synchronization | Shared monotonic timestamps, asynchronous watermarks and audio gap filling |
| Concurrent file I/O | A separate worker muxes a protected snapshot while capture continues |
| Resource ownership | Pooled frames, native capture/codec objects, snapshot leases and orderly shutdown |

Use `ARCHITECTURE.md` for the thread/ownership map. FFmpeg supplies H.264/AAC codecs and MP4 muxing; Windows/NAudio supply capture. Our implementation owns queues, ring storage, eviction, snapshot protection, synchronization and lifecycle coordination.

## Report measured results accurately

The 30-minute fixture ran on a Core i3-7100U with a 1366×728 animated capture source. It averaged 29.997 encoded FPS and 18.68% process CPU, with +5.49 MiB median private-memory growth after warm-up. The diagnostic UI is included in that CPU measurement. These are controlled local results; no 1080p, 4K or game-performance claim follows from them.

Do not describe the 128 MiB ring budget as a process-memory limit: raw inputs, native codec/GPU state, runtime memory and export-held packets add to it. At most one export snapshot is allowed, accounting for at most another ring budget.
