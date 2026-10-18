# Replay engine — 2026-10-15

## Implemented behavior

Start buffering selects a Windows capture source and starts continuous H.264 encoding plus optional AAC system audio. Encoded packets go into our own circular array; no session-length video file is written. Save Replay fixes a trigger cutoff, waits for the already-captured video and delayed audio packets to reach that point, and leases references to the selected immutable packets. A separate worker remuxes them into a fresh MP4 without re-encoding or holding the ring lock.

The app supports 15/30/60-second history selection, early saves with partial history, repeated saves, visible buffer/resource statistics, and graceful stopping. Source/profile settings are held fixed while a session is running. Save failures leave capture running. An existing destination is never overwritten. A final filename appears only after the MP4 trailer is written and the file is closed.

## Bounds and synchronization

- The compressed ring has a 128 MiB accounted-byte limit and 8,192 packet slots, plus time-based eviction. Accounted bytes include a conservative per-packet metadata allowance.
- Eviction preserves a usable video keyframe and a small AAC preroll. Byte/slot pressure drops complete older groups; if a single group cannot fit, non-keyframe packets are discarded until the next decodable restart.
- Save uses only complete video frames ending at or before the button press. It can start earlier than the requested lookback to include a decodable keyframe. The result reports its actual start, end and duration.
- Packet payloads are copied out of FFmpeg-owned memory once, then shared by reference between the ring and one protected snapshot. Ring eviction cannot mutate or release the snapshot's data. The lease is released on completion, cancellation or failure.
- At most one snapshot/export is allowed. Its accounted data is at most one ring budget, so active-ring plus snapshot references have a conservative upper bound of 256 MiB, excluding the fixed reference arrays, raw capture queues, codec/GPU state and one native output packet.
- Watermark waiters use asynchronous signals. No file I/O or codec/muxer work happens while the ring lock is held.
- Capture/encoding continues while a snapshot is exported. On Stop, capture and audio devices are released before waiting for an existing export; immutable codec parameters and snapshot data remain valid until that export ends.

## Verification

Sixteen automated tests pass: eight queue/audio tests plus eight ring tests covering decoder boundaries, exclusion of future frames, partial history, snapshots surviving eviction/disposal, duration retention, byte pressure, slot pressure, a single lease, delayed audio watermarks and cancellation/disposal of waiters.

The controlled replay fixture uses an early white marker at 2 seconds, a retained white marker and quiet 600 Hz tone at 5 seconds, and a magenta marker after the first Save trigger. For the default profile:

| Check | Observed result |
|---|---|
| Partial history | Save at 1.608 s produced 1.600 s and 48 decoded frames; no later marker appeared |
| Actual 30-second profile | Save at 34.606 s produced footage from 4.000–34.600 s: 30.600 s, 918 decoded frames |
| Correct pre-trigger content | The 5-second white marker was present; the evicted 2-second marker and later magenta marker were absent |
| Protected snapshot | Fixture deliberately held export for four seconds; 121 additional capture frames arrived after snapshot acquisition |
| Concurrent eviction | While the snapshot was held, 609 older packets had been evicted; active ring usage was 1,357,960 accounted bytes and pinned snapshot usage was 1,382,425 bytes |
| Audio alignment | The replay tone preceded its white marker by about 90 ms, inside the 150 ms controlled-fixture acceptance limit |
| Repeated Save | A later clip covered 9.000–39.400 s, with 912 decoded frames, and included the new post-save magenta marker |
| Stop during export | The final 30.633-second clip decoded all 919 frames after capture stopped; the saved snapshot completed independently |
| Save errors | Busy requests and an existing destination were rejected; a cancelled save published no clip; a later Save still succeeded |

Every clip was independently checked with ffprobe and fully decoded with FFmpeg in error-fatal mode. Video timestamps were rebased to zero; AAC and video durations agreed within 50 ms. A separate video-only run passed the same partial-history, snapshot/eviction, repeated-save and stop/export checks without an audio stream. The short three-second audio fixture also passed and measured approximately 57 ms A/V offset.

The four-second export delay is an internal diagnostic stall, not normal export latency. In the default-profile run the normal repeated export took approximately 19 ms after snapshot acquisition. These tests use controlled windows and do not replace the upcoming sustained full-screen performance benchmark.

## Reproduce

```powershell
.\scripts\test.ps1
.\scripts\replay-proof.ps1
.\scripts\replay-proof.ps1 -DefaultDuration
.\scripts\replay-proof.ps1 -VideoOnly
```

Each fixture writes only to an ignored `artifacts/replay-proof-<id>/` directory. It saves metrics in `replay.json`, stream metadata, decoded marker analysis and `verification.json`. Recheck a saved fixture with `scripts/verify-replay.ps1 -Directory <fixture-directory>`.

## Remaining milestones

Global hotkey and tray workflow, sustained CPU/memory testing, broader source/device/error coverage and final packaging/documentation remain. No project files or binaries have been pushed to GitHub.
