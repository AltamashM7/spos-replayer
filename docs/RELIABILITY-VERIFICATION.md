# Reliability verification — 2026-10-16

## Changes

System-audio capture now subscribes to endpoint notifications for its selected default multimedia playback device. A changed/missing default, selected device removal or inactive state records an actionable failure. Notification callbacks do not block or tear down the audio stack. The session observes the failure and releases capture through its existing shutdown path. Unexpected audio recorder termination is also reported instead of silently recording indefinite silence. Source closure now faults a pending first-frame wait promptly.

Tray updates and notifications ignore late callbacks after disposal, and disposal is idempotent. The main window fits the desktop work area; shorter windows scroll their content so controls remain reachable. This corrects the previous 820-pixel window height on the target display's 728-pixel work area.

## New focused checks

- Seven endpoint-policy tests passed. They distinguish relevant output changes from unrelated capture/communications devices, cover a missing default and disabled/unplugged/removed devices, and preserve the first reported failure.
- Ten native failure checks passed: invalid save directory, locked output, recovery after failed saves, actual capture-window resize and closure, an injected endpoint change through a real audio/video session, deliberately slowed video encoding, catch-up, repeated shutdown and temporary-file cleanup.
- The overloaded session reported 105 dropped queued frames at 4.06 seconds, kept running, caught up after the injected delay was removed, and exported a six-second clip with 180 video frames and a 48 kHz stereo AAC track.
- Both failure-recovery clips fully decoded using FFmpeg with `-xerror`; the audio/video clip had matching six-second track durations.
- Ten live workflow checks passed: locked capture preferences, minimizing live capture, hidden-window hotkey dispatch, ignoring a repeated hotkey while busy, tray restore and save, tray Exit while an export holds a snapshot, and harmless late callbacks/repeated disposal after exit. All three resulting clips fully decoded. Exit released the tray/shortcut and left no partial file. The affected workflow fixture was rerun after adding the disposal guard.
- Six focused layout checks passed at window heights of 728 and 540 logical pixels. The default layout fits without scrolling; the compact layout scrolls with its footer reachable. Both rendered screenshots were visually reviewed.

The live workflow fixture supplies its own capture source and dispatches WM_HOTKEY directly; it does not drive the Windows source picker or synthesize physical keyboard input. Endpoint tests simulate notification data rather than changing the machine's output device. Those distinctions are covered by the [manual checks](MANUAL-CHECKS.md).

Evidence directories (ignored local artifacts):

- Failure checks: `artifacts/reliability-81a00403a3234319bc2a6146914e4e18`.
- Live workflow with the shutdown guard: `artifacts/workflow-f8ffb7e238304157a1d59e21cf6fd805`.
- Layout: `artifacts/desktop-f07d08d65ca142a78e548f2513c69252`.

The previously passing queue, ring, timestamp, pre-trigger and idle desktop checks were not rerun. The new native tests exercise the changed audio/source failure paths and newly integrated live command paths.

## Sustained measurement

The 30-minute run passed at `artifacts/reliability-e608ed8f076545d0905eb6ae517d07e9`. It captured only its own animated 1366×728 window with system audio and a 30-second replay history. It recorded 358 samples, flushed approximately every five seconds to `resources.jsonl`.

The fixture records private/working-set/managed memory, handles, threads, normalized total-process CPU, captured/encoded/dropped frames and ring/snapshot accounting. CPU includes the animated diagnostic UI in the same process. It verifies storage bounds on every sample, at least 28.5 encoded FPS overall, recent retained history, shutdown within eight seconds, median private-memory growth below 64 MiB and median handle growth below 32 after warm-up. These are local acceptance thresholds, not hardware-independent performance guarantees. It does not force garbage collection or trim the working set.

Target machine: Intel Core i3-7100U at 2.40 GHz, four logical processors, Windows 10 build 19045. Results:

| Measurement | Observed |
|---|---|
| Capture elapsed time | 1,800.061 seconds |
| Encoded frames / average output rate | 53,996 / 29.997 FPS |
| Captured frames / average delivery rate | 53,786 / 29.880 FPS |
| Dropped queued video frames | 38 over the full run |
| Average process CPU | 18.68% of total logical CPU capacity, including the animated diagnostic UI |
| Peak sampled private memory during capture | 222.91 MiB |
| Median private-memory growth after warm-up | +5.49 MiB, below the 64 MiB acceptance threshold |
| Median handle-count change after warm-up | −6 |
| Final active ring | 23.35 MiB, 2,378 packets, 30.867 seconds of history |
| Evicted compressed packets | 135,986 |
| Final replay interval | 1,770.000–1,800.067 seconds |
| Final replay | 902 H.264 frames; 30.066667-second video and AAC tracks; 48 kHz stereo audio |
| Snapshot-to-completed-export time | 289 ms, excluding the earlier watermark wait |
| Session shutdown time | 1.383 seconds |

Every sampled ring/snapshot/slot budget check passed. The final replay fully decoded with FFmpeg `-xerror`; its inspected track durations match. Captured-frame delivery also exceeded 28.5 FPS in a separate report assertion. No long-session video file was written: only the final replay was exported. No garbage collection or working-set trimming was forced.

This is a controlled-window result at the target display's available resolution. It does not certify 1080p/4K, games, exclusive fullscreen, GPU-memory behavior, physical device changes or visible notification delivery. Physical keyboard/audio/presentation checks remain pending in [manual checks](MANUAL-CHECKS.md).

## Reproduce

```powershell
.\scripts\reliability-proof.ps1              # Short native failure suite
.\scripts\reliability-proof.ps1 -Minutes 30  # Sustained capture and final-clip decode
.\scripts\workflow-proof.ps1                 # Live hotkey/tray commands during capture/export
```

Use `-SkipBuild` only after building the current code. Run only the fixtures relevant to changed behavior. The resource run writes a small final replay, not a 30-minute recording. System audio is still the output mix, so unrelated audio playing on the PC can be included in that local artifact.
