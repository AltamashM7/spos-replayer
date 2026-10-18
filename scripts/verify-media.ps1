param([Parameter(Mandatory=$true)][string]$Directory, [switch]$VideoOnly, [switch]$Silence)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDirectory = (Resolve-Path -LiteralPath $Directory).Path
$taskBin = Join-Path $taskRoot '.tools\ffmpeg\ffmpeg-9.0.2-full_build-shared\bin'
$taskClip = Join-Path $taskDirectory 'sample.mp4'
$taskProbe = & (Join-Path $taskBin 'ffprobe.exe') -v error -count_frames -show_streams -show_format -of json $taskClip
if ($LASTEXITCODE -ne 0) { throw 'ffprobe could not read the clip.' }
$taskProbeText = $taskProbe -join [Environment]::NewLine
$taskProbeText | Set-Content -LiteralPath (Join-Path $taskDirectory 'probe.json')
$taskMetadata = $taskProbeText | ConvertFrom-Json
$taskVideo = $taskMetadata.streams | Where-Object codec_type -eq 'video'
$taskAudio = $taskMetadata.streams | Where-Object codec_type -eq 'audio'
if ($taskVideo.codec_name -ne 'h264' -or [int]$taskVideo.nb_read_frames -ne 240) { throw 'Expected 240 H.264 frames.' }
if ($taskVideo.width -gt 1920 -or $taskVideo.height -gt 1080 -or $taskVideo.width % 2 -or $taskVideo.height % 2) { throw 'Unexpected video geometry.' }
$taskVideoSeconds = [double]::Parse($taskVideo.duration, [Globalization.CultureInfo]::InvariantCulture)
if ([Math]::Abs($taskVideoSeconds - 8) -gt 0.04) { throw 'Video duration is incorrect.' }
if ($VideoOnly) {
    if ($taskAudio) { throw 'Video-only recording contains audio.' }
} else {
    if ($taskAudio.codec_name -ne 'aac' -or $taskAudio.sample_rate -ne '48000' -or $taskAudio.channels -ne 2) { throw 'Expected 48 kHz stereo AAC.' }
    $taskAudioSeconds = [double]::Parse($taskAudio.duration, [Globalization.CultureInfo]::InvariantCulture)
    if ([Math]::Abs($taskAudioSeconds - $taskVideoSeconds) -gt 0.05) { throw 'Audio/video durations differ.' }
}
& (Join-Path $taskBin 'ffmpeg.exe') -v error -threads 1 -i $taskClip -map 0:v:0 -vf 'scale=16:16,format=gray' -f rawvideo -y (Join-Path $taskDirectory 'video.gray')
if ($LASTEXITCODE -ne 0) { throw 'Video decoding failed.' }
$taskPixels = [IO.File]::ReadAllBytes((Join-Path $taskDirectory 'video.gray'))
$taskVideoOnsets = [Collections.Generic.List[double]]::new()
$taskPrevious = $false
for ($taskFrame = 0; $taskFrame -lt $taskPixels.Length / 256; $taskFrame++) {
    $taskSum = 0
    for ($taskPixel = 0; $taskPixel -lt 256; $taskPixel++) { $taskSum += $taskPixels[$taskFrame * 256 + $taskPixel] }
    $taskFlash = $taskSum / 256 -gt 160
    if ($taskFlash -and -not $taskPrevious) { $taskVideoOnsets.Add($taskFrame / 30.0) }
    $taskPrevious = $taskFlash
}
if ($taskVideoOnsets.Count -ne 2) { throw 'The captured video does not contain the two expected screen flashes.' }
$taskAudioOnsets = [Collections.Generic.List[double]]::new()
$taskOffsets = [Collections.Generic.List[double]]::new()
if (-not $VideoOnly) {
    & (Join-Path $taskBin 'ffmpeg.exe') -v error -threads 1 -i $taskClip -map 0:a:0 -ac 1 -ar 48000 -f f32le -y (Join-Path $taskDirectory 'audio.f32')
    if ($LASTEXITCODE -ne 0) { throw 'Audio decoding failed.' }
    $taskPcm = [IO.File]::ReadAllBytes((Join-Path $taskDirectory 'audio.f32'))
    # Detect the fixture's 600 Hz tones in 10 ms windows with a Goertzel filter.
    $taskCoefficient = 2 * [Math]::Cos(2 * [Math]::PI * 600 / 48000)
    $taskPrevious = $false
    for ($taskBlock = 0; $taskBlock -lt [Math]::Floor($taskPcm.Length / 4 / 480); $taskBlock++) {
        $taskS1 = 0.0; $taskS2 = 0.0
        for ($taskSample = 0; $taskSample -lt 480; $taskSample++) {
            $taskS0 = [BitConverter]::ToSingle($taskPcm, ($taskBlock * 480 + $taskSample) * 4) + $taskCoefficient * $taskS1 - $taskS2
            $taskS2 = $taskS1; $taskS1 = $taskS0
        }
        $taskAmplitude = 2 * [Math]::Sqrt([Math]::Max(0, $taskS1 * $taskS1 + $taskS2 * $taskS2 - $taskCoefficient * $taskS1 * $taskS2)) / 480
        $taskTone = $taskAmplitude -gt 0.003
        if ($taskTone -and -not $taskPrevious) { $taskAudioOnsets.Add($taskBlock / 100.0) }
        $taskPrevious = $taskTone
    }
    if (-not $Silence) {
        if ($taskAudioOnsets.Count -ne 2) { throw "Expected two test tones, found $($taskAudioOnsets.Count)." }
        for ($taskPulse = 0; $taskPulse -lt 2; $taskPulse++) {
            $taskOffset = ($taskAudioOnsets[$taskPulse] - $taskVideoOnsets[$taskPulse]) * 1000
            if ([Math]::Abs($taskOffset) -gt 150) { throw "A/V synchronization exceeds 150 ms: $taskOffset ms" }
            $taskOffsets.Add($taskOffset)
        }
    }
}
$taskResult = [ordered]@{
    DecodePassed = $true
    VideoSeconds = $taskVideoSeconds
    VideoFrames = [int]$taskVideo.nb_read_frames
    VideoFlashSeconds = @($taskVideoOnsets)
    AudioToneSeconds = @($taskAudioOnsets)
    AudioMinusVideoMilliseconds = @($taskOffsets)
    VideoOnly = [bool]$VideoOnly
    SilenceFixture = [bool]$Silence
}
$taskJson = $taskResult | ConvertTo-Json -Depth 4
$taskJson | Set-Content -LiteralPath (Join-Path $taskDirectory 'verification.json')
$taskJson
