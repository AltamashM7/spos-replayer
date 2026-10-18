# Rewind build plan

## Agreed scope

- Windows desktop application written in C#.
- Record the screen and system audio.
- Retain recent footage in bounded storage and save it on demand.
- Capture must continue during export.
- Implement genuine concurrency, producer-consumer coordination, synchronization, circular buffering, resource management and file I/O.
- Build and verify locally; push to `AltamashM7/spos-replayer` when finished.

## Working architecture

WPF on .NET 10 LTS, Windows Graphics Capture for picker-selected display/window frames, DXGI Desktop Duplication for border-free full-monitor frames, WASAPI loopback for audio, and pinned FFmpeg libraries for H.264/AAC encoding and MP4 muxing. Live encoding and saved-clip muxing have independent state.

```text
screen capture -> bounded frame queue -> video encoder --+
                                                        +-> compressed replay ring
system audio   -> bounded audio queue -> audio encoder --+          |
                                                              save request
                                                                   |
                                                        protected packet snapshot
                                                                   |
                                                          export worker -> MP4
```

Our code owns the queues, replay buffer, eviction, synchronization and snapshot lifetimes. Codec/container libraries provide media encoding and muxing.

## Milestones

1. **Local foundation (complete):** project-local SDK, reproducible build scripts, WPF startup, capture capability probe and replay preferences.
2. **Media proof (complete):** Windows screen capture, output-device audio and a playable MP4. Verified full decoding, silence handling, timestamps, A/V alignment and cancellation cleanup. See [verification](MEDIA-VERIFICATION.md).
3. **SPOS engine (complete):** bounded synchronized queues, a circular compressed packet array, keyframe-aware eviction, byte/slot/time limits and a single protected snapshot lease. Verified by unit and native integration tests.
4. **Instant replay (complete):** background export of the preceding interval while capture continues; partial history, repeated saves and stop/export coordination. Verified on the actual 30-second profile. See [replay verification](REPLAY-VERIFICATION.md).
5. **User workflow (complete):** source selection, start/stop, Save Replay, export state, buffer statistics, configurable global shortcuts and tray controls. Nineteen focused desktop checks verify registration conflicts, preference compatibility, command availability and tray exit cleanup. See [desktop verification](DESKTOP-VERIFICATION.md).
6. **Reliability (automated checks complete; manual checks pending):** seven endpoint-policy tests, ten native failure checks, ten live command/shutdown checks, six layout checks and a passing 30-minute resource run. See [measurements](RELIABILITY-VERIFICATION.md) and [manual checks](MANUAL-CHECKS.md).
7. **Delivery (application accepted; source delivery authorized):** self-contained Windows x64 ZIP, source snapshot, dependency provenance/notices, file hashes and a demonstration guide are prepared. Isolated packaged startup and both capture modes passed. User acceptance and authorization to push the finished source to `AltamashM7/spos-replayer` were recorded on 2026-10-18. Executable packages remain local. Public binary distribution still requires the corresponding source/license materials and a project-license decision. See [delivery](DELIVERY.md) and [package verification](PACKAGE-VERIFICATION.md).

User feedback led to border-free full-monitor capture and saved-audio quality corrections. Focused checks pass, and the user confirmed the updated build works. See [quality verification](QUALITY-VERIFICATION.md). The earlier 30-minute resource figures describe Windows Graphics Capture, not the new DXGI backend. Physical device changes remain unverified and are documented separately from functional completion.

## Initial profile

- Replay lengths: 15, 30 or 60 seconds; default 30.
- Target 30 FPS, scale to at most 1080p while preserving aspect ratio.
- MP4 with H.264 video and AAC system audio.
- System audio is the selected output device's mix, including other apps even when recording one window.
- One concurrent export initially. Additional requests receive a clear busy status.
- A clip can start slightly earlier than the selected duration to preserve its preceding decodable keyframe. Report actual duration.

The replay recorder implements source selection, scaling and the planned controls. Audio uses the default multimedia playback endpoint chosen at session start; a device change stops that session explicitly. A separate endpoint picker is not implemented. The finite sample recorder remains as a regression fixture.

## Engine constraints

- Bound raw frame/audio queues and compressed storage separately.
- Account for snapshots retaining packets after ring eviction; limit both active-ring and export-held memory.
- Preserve monotonic timestamps when dropping overloaded video input.
- Synchronize audio/video to a shared monotonic timeline, including encoder delay.
- Fill silent audio gaps and handle clock drift without shortening quiet periods.
- Use keyframe-aware start selection and a defined save-time cutoff. Handle packets still in the encoding pipeline at the trigger.
- Keep disk I/O and long work outside capture callbacks and shared-buffer locks.
- Define owned frame/packet lifetimes and release native resources on every shutdown/error path.
- Write exports to temporary files and publish a final filename only after successful container finalization.
- Treat source/device changes as explicit state transitions; do not combine incompatible streams silently.

## Completion evidence

Demonstrate pre-trigger events in saved clips, uninterrupted capture during export, synchronized audio, stable memory over prolonged operation, graceful overload and save failures, and clean resource teardown. Record CPU usage, dropped frames, retained duration and export latency on the target machine.

## Foundation versions

- .NET SDK: 10.0.402, installed under `.tools/dotnet`.
- Framework: `net10.0-windows10.0.19041.0` for WPF; `net10.0` for core.
- Architecture: x64.
- FFmpeg.AutoGen 9.0.1.1 with checksum-pinned FFmpeg 9.0.2 shared binaries.
- NAudio.Wasapi 3.1.0; Vortice.Direct3D11 3.8.3.
- All project dependencies are locked in `packages.lock.json`; native dependencies are recorded in `native-dependencies.json`.
