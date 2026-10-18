"""Inspect controlled diagnostic clips; requires Python + NumPy, not needed by Rewind."""
import argparse
import json
import subprocess
from pathlib import Path

import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("--baseline", required=True, type=Path)
parser.add_argument("--audio", required=True, type=Path)
parser.add_argument("--monitor", required=True, type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
ffbin = root / ".tools/ffmpeg/ffmpeg-9.0.2-full_build-shared/bin"


def decode(path, *options):
    return subprocess.run([str(ffbin / "ffmpeg.exe"), "-v", "error", "-xerror", "-threads", "1",
                           "-i", str(path), *options, "-"], check=True, capture_output=True).stdout


def audio(path, frequency=730):
    pcm = np.frombuffer(decode(path, "-map", "0:a:0", "-ac", "1", "-ar", "48000", "-f", "f32le"), dtype="<f4")
    # Ignore the intentional start/stop of the tone, and fit a free phase in each 10 ms block.
    steady = pcm[48000:-48000].astype(float)
    count = len(steady) // 480
    blocks = steady[:count * 480].reshape(count, 480).T
    phase = np.arange(480) * 2 * np.pi * frequency / 48000
    basis = np.array([np.sin(phase), np.cos(phase), np.ones(480)]).T
    fit = np.linalg.lstsq(basis, blocks, rcond=None)[0]
    rms = np.sqrt(np.mean(blocks ** 2, axis=0))
    residual = np.sqrt(np.mean((blocks - basis @ fit) ** 2, axis=0)) / np.maximum(rms, 1e-10)
    return {
        "PeakDbfs": float(20 * np.log10(max(np.max(np.abs(pcm)), 1e-20))),
        "MedianNormalizedToneResidual": float(np.median(residual)),
        "P95NormalizedToneResidual": float(np.percentile(residual, 95)),
        "MaximumNormalizedToneResidual": float(np.max(residual)),
        "BlocksAbove20PercentResidual": int(np.count_nonzero(residual > .2)),
        "SteadyBlocks": count,
    }


baseline = audio(args.baseline / "audio-quality.mp4")
updated = audio(args.audio / "audio-quality.mp4")
headroom = audio(args.audio / "headroom.mp4", 997)
observations = json.loads((args.audio / "audio-quality.json").read_text(encoding="utf-8-sig"))
assert all(x["Frames"] == 0 for x in observations["DeviceDeltas"]), "Capture lost device samples"
assert observations["JitterAdjustedPackets"] > 0, "Timing fix was not exercised"
assert updated["P95NormalizedToneResidual"] < baseline["P95NormalizedToneResidual"], "Tone residual did not improve"
assert updated["BlocksAbove20PercentResidual"] == 0, "Saved tone has a large discontinuity"
assert headroom["PeakDbfs"] < 0, "Loud synthetic input clips after AAC decoding"

monitor_report = json.loads((args.monitor / "monitor.json").read_text(encoding="utf-8-sig"))
monitor_checks = []
for index, replay in enumerate(monitor_report["Replays"]):
    path = Path(replay["File"])
    decode(path, "-f", "null")  # Fully decode every video/audio packet, not just the sampled frames.
    probe = json.loads(subprocess.run([str(ffbin / "ffprobe.exe"), "-v", "error", "-show_streams", "-of", "json", str(path)],
                                      check=True, capture_output=True).stdout)
    video = next(x for x in probe["streams"] if x["codec_type"] == "video")
    assert video["codec_name"] == "h264"
    assert any(x["codec_type"] == "audio" and x["codec_name"] == "aac" for x in probe["streams"]) == (index == 0)
    width, height = video["width"], video["height"]
    frames = np.frombuffer(decode(path, "-map", "0:v:0", "-vf", "select=eq(n\\,10)+eq(n\\,80)",
                                "-fps_mode", "passthrough", "-pix_fmt", "rgb24", "-f", "rawvideo"), dtype=np.uint8)
    frames = frames.reshape(2, height, width, 3)
    colors = []
    for frame, expected in zip(frames, ([210, 30, 40], [30, 80, 210])):
        edge = np.concatenate((frame[0], frame[-1], frame[:, 0], frame[:, -1])).astype(int)
        error = float(np.percentile(np.max(np.abs(edge - expected), axis=1), 99))
        yellow = int(np.count_nonzero((edge[:, 0] > 160) & (edge[:, 1] > 140) & (edge[:, 2] < 100)))
        assert error < 20 and yellow == 0, "Capture edge differs from fixture; possible outline or incorrect monitor geometry"
        colors.append({"EdgeP99ColorError": error, "YellowEdgePixels": yellow})
    monitor_checks.append({"File": str(path), "Width": width, "Height": height, "FullDecodePassed": True, "Frames": colors})

result = {"Passed": True, "Baseline": baseline, "Updated": updated, "Headroom": headroom,
          "JitterAdjustedPackets": observations["JitterAdjustedPackets"], "Monitor": monitor_checks}
text = json.dumps(result, indent=2)
(args.audio / "quality-verification.json").write_text(text, encoding="utf-8")
print(text)
