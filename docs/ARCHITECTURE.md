# Rewind architecture and SPOS concepts

## Data flow and thread ownership

```text
WGC callback / DXGI capture worker            WASAPI audio callback
    | owned BGRA frame                            | owned PCM packet
    v                                             v
bounded frame queue (12)                       bounded audio queue (64)
    |                                             |
    +-------------- encoder worker ---------------+
                    | H.264 / AAC packets
                    v
           synchronized circular packet array
                    | save-time cutoff
                    v
          one protected immutable snapshot
                    | independent export worker
                    v
           temporary MP4 -> final filename
```

The WPF dispatcher owns source selection, control state and the capture session's lifecycle. Window/display picker mode uses Windows Graphics Capture with a free-threaded frame pool. Its callback copies a staging texture to pooled memory. Full-monitor mode instead uses a dedicated DXGI Desktop Duplication worker, creates its D3D device on the monitor's adapter, copies BGRA frames and composes separate hardware cursors. It creates no Windows Graphics Capture session and has no system capture border. Both implement `IFrameSource` and transfer frames into the same bounded queue. DXGI mode currently rejects rotated displays. WASAPI callbacks transfer timestamped stereo PCM into the audio queue. Capture producers do not write clips to disk.

`ReplaySession.PumpAsync` runs the encoding loop on a thread-pool worker. It owns the encoder objects, the current/pending video frames and the audio timeline reader, which holds at most two audio packets outside the queue. Video and audio advance on the same monotonic QPC timeline. For continuous device sample positions, the reader rejects sub-millisecond timestamp jitter and follows filtered clock drift through small interpolated corrections. Actual gaps and discontinuities retain their timestamp placement and silence. WASAPI uses a 100 ms buffer and MMCSS Audio priority. AAC uses 192 kbps and a fixed 0.5 gain for recording headroom. Video keeps a fixed 30 FPS output timeline and can reuse the most recent captured frame, including a static DXGI desktop. Under input pressure the queues discard old inputs instead of growing without a limit.

Save runs independently of live encoding. It first waits asynchronously for encoded video/audio watermarks to reach a fixed trigger cutoff, then leases immutable compressed packets from the ring. A separate worker muxes those packets into MP4. A snapshot keeps its packet objects alive even if the live ring evicts them. Capturing and encoding continue during export.

## Synchronization and ownership

| Component | Coordination | Owned resources / bound |
|---|---|---|
| `BoundedOwnedQueue<T>` | Monitor lock for enqueue/dequeue/dispose; disposes dropped items outside the lock | 12 raw video frames or 64 audio packets per respective queue |
| `ScreenCaptureSource` | Callback/disposal gate; atomic counters and published errors | D3D device/context, capture pool/session, staging texture and frame queue |
| `DesktopCaptureSource` | One worker owns duplication/context; bounded acquire wait; atomic stop and published errors | Monitor-adapter D3D device, DXGI duplication, staging texture, pointer shape capped at 4 MiB and frame queue |
| `SystemAudioSource` | Nonblocking endpoint notification handlers publish failure; normal lifecycle performs teardown | Recorder, fixed output endpoint, notification subscription and audio queue |
| `PacketRingBuffer` | Monitor lock protects circular indices, byte accounting, watermarks and the snapshot lease | 8,192 slots, 128 MiB accounted active storage, selected 15/30/60-second history |
| `ReplaySnapshot` | One protected lease; immutable packet references shared with the ring | At most another ring budget of packet references/payloads held for export |
| `ReplaySession` | Lifecycle lock prevents overlapping saves; atomic disposal transition; cancellation tokens and asynchronous completion signals | Encoder pump, one active export, codec configuration and source lifetimes |
| `MainWindow` | WPF dispatcher plus start/stop/save guards | Button/tray/hotkey commands and visible operation state |

The 256 MiB combined ring/snapshot accounting ceiling is not a process-memory limit. Raw frame arrays, audio buffers, native codec state, D3D resources, managed runtime memory and diagnostic UI are additional allocations. Frame arrays are rented from `ArrayPool`; returning them permits reuse and does not promise an immediate reduction in process working set.

No shared-buffer lock covers filesystem I/O or codec/muxer work. Watermark waiters use asynchronous completion signals, so a save waiting for encoded packets does not block the producer's lock. Eviction advances to decodable video keyframes with AAC preroll. A replay may therefore begin slightly before the selected lookback; the UI reports its actual length.

## Failure and shutdown policy

- A source closure, source geometry change, default multimedia playback-device change or selected endpoint loss ends that recording session. The UI reports the reason and asks the user to start buffering again. Streams with incompatible geometry or a different output device are not combined into one recording.
- Audio endpoint notification handlers only record an error. They do not close the audio stack from inside a notification callback. The encoder loop observes the error, and the owning session performs teardown.
- A failed or cancelled Save releases its snapshot. An existing destination remains untouched. A final filename appears only after the temporary container is finalized and closed; temporary files are removed on failure.
- Stop cancels and joins the encoder pump, releases audio/capture sources, then waits for any export that already owns a snapshot. Codec parameters and the ring stay valid until that export finishes. Close and tray Exit use the same cleanup path, then release the tray icon and global shortcut.
- Overload can drop queued capture frames and shorten history under compressed-byte pressure. It cannot expand queue capacity or ring accounting limits. If encoding cannot reach the save cutoff within ten seconds, Save reports a timeout while leaving the session available to stop or recover.

## Source map

`src/Rewind.Core` implements the reusable bounded queues, encoded packet model, ring, snapshot and budgets. `src/Rewind.Media` owns Windows capture/audio integration, pooled input buffers, timing, encoding, replay lifecycle and MP4 export. `src/Rewind.App` supplies WPF controls, settings, shortcut/tray integration and opt-in diagnostic fixtures. `tests/Rewind.Tests` covers coordination, buffer semantics, audio timing and endpoint-change policy.

These are operating-system concepts implemented by application code: producer/consumer coordination, mutual exclusion, bounded buffering, asynchronous signaling, cancellation, reference ownership and concurrent file I/O. FFmpeg provides codec/container algorithms; Windows and NAudio provide capture/device APIs. Rewind does not implement a kernel scheduler, a codec or a graphics driver.
