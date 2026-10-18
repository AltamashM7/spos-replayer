# Media proof — 2026-10-15

## Implemented path

The C# application captures display/window frames with Windows Graphics Capture, copies BGRA data through a Direct3D staging texture into owned pooled buffers, and sends it through a bounded synchronized queue. A worker scales/converts frames and encodes H.264 through FFmpeg's library API.

WASAPI loopback capture delivers 48 kHz stereo floating-point PCM into a separate bounded queue. Device QPC timestamps place samples on the same monotonic timeline as video. Missing callbacks produce silence for the elapsed time, while late/overlapping samples do not move subsequent audio earlier. The worker encodes AAC and writes interleaved MP4 packets. A final filename is published only after successful muxer finalization and file closure.

Raw queues hold at most 12 video frames and 64 audio packets, plus explicitly held current/pending frames and packets. The scheduled encoder stays 200 ms behind capture to allow data delivery. These are finite bounds; full-screen/4K CPU and memory tuning belongs to the resource/performance milestone.

Windows 10 can report initial capture-item bounds that include margins absent from the first frame. Capture geometry is therefore established from the first actual frame. Later size changes stop the sample safely. Windows capture sessions are released on their owning UI thread, while encoding and file I/O run off that thread.

Video has explicit frame/packet durations and one-second keyframe intervals. The software H.264 encoder uses no B-frames, simplifying the forthcoming replay packet buffer.

## Verified locally

Environment: Windows 10 build 19045, x64, .NET runtime 10.0.13. System audio endpoint: Speaker / Headphone (Realtek(R) Audio).

- Build: zero warnings and errors.
- Unit tests: 8 passing. Covers queue overflow ownership, concurrent producers/consumer, shutdown during writes, full silence, leading/middle gaps, late packets and partial/overlapping packets.
- Controlled window recording: 8 seconds, 666 × 412 pixels, 240 decoded H.264 frames at 30 FPS, stereo 48 kHz AAC. The window contains two flashes; the output device plays two quiet 600 Hz tones. Complete video and audio decode passed.
- Measured synchronization in the successful full fixture: video flashes at 2.0667 s and 5.0667 s; audio tones at 2.01 s and 5.01 s. Audio preceded video by approximately 56.7 ms at both markers (150 ms fixture acceptance limit).
- Final full fixture queue drops: zero video and zero audio. Maximum observed frame delivery delay in that run: 17.0 ms.
- Video-only fixture: 240 decodable frames, eight-second video, no audio stream.
- Silence fixture: no WASAPI samples arrived; 384,000 silence frames were generated and the eight-second stereo AAC track remained aligned with the eight-second video.
- Cancellation fixture: cancellation observed, process exited cleanly, and neither a final MP4 nor a partial MP4 remained.

The timing fixture's tone source uses its monotonic test clock so a playback underrun cannot shift later tone generation. The checker decodes video/audio independently, detects flashes and tone onsets, and compares their timestamps. This is a short controlled proof, not a sustained full-screen performance benchmark.

## Reproduce

Run `scripts/test.ps1` for pure tests. `scripts/media-proof.ps1` creates an ignored `artifacts/media-proof-<id>/` directory containing the clip, recording metrics, ffprobe metadata, decoded analysis data and verification JSON. Pass `-VideoOnly`, `-Silence` or `-Cancel` for those cases. The fixtures capture only their own diagnostic window; the full fixture plays two quiet test tones.

`scripts/verify-media.ps1 -Directory <fixture-directory>` reruns decoding and timing checks on an existing full fixture; use the corresponding `-VideoOnly` or `-Silence` switch for those recordings.

## Current boundary

This document records the original finite-media proof. The app now implements pre-trigger replay; see [replay verification](REPLAY-VERIFICATION.md). The finite recorder is retained for regression checks and shares the live encoder and packet muxer with replay. Monitor/window selection is implemented; the automated fixtures exercise their controlled windows. Long-run memory/CPU measurements, broad monitor/resolution tests, audio endpoint selection, device-switch recovery, HDR handling, tray controls and global hotkeys remain future milestones. Sources larger than approximately 4K are rejected in this profile; resizing or closing a source stops capture rather than mixing incompatible frames.
