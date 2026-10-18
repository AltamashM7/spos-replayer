param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDirectory = (Resolve-Path -LiteralPath $Directory).Path
$taskBin = Join-Path $taskRoot '.tools\ffmpeg\ffmpeg-9.0.2-full_build-shared\bin'
$taskReport = Get-Content -LiteralPath (Join-Path $taskDirectory 'replay.json') -Raw | ConvertFrom-Json
if (-not ($taskReport.BusyRejected -and $taskReport.ExistingFileRejected -and $taskReport.CancellationPassed)) { throw 'A save error/cancellation check failed.' }
if ($taskReport.Replay.CapturedAfterExport - $taskReport.Replay.CapturedAtSnapshot -lt 60) { throw 'Capture did not advance during the held export.' }
if ($taskReport.WhileSnapshotPinned.Buffer.EvictedPackets -lt 1) { throw 'The snapshot was not tested against eviction.' }
if ($taskReport.WhileSnapshotPinned.Buffer.RetainedBytes -gt $taskReport.WhileSnapshotPinned.Buffer.MaximumBytes) { throw 'Buffer exceeded its byte budget.' }
if ($taskReport.Replay.StartSeconds -le 2.3 -or $taskReport.Repeated.StartSeconds -le $taskReport.Replay.StartSeconds) { throw 'Rolling eviction did not advance the saved interval.' }
if ($taskReport.LookbackSeconds -and ($taskReport.Replay.DurationSeconds -lt $taskReport.LookbackSeconds -or $taskReport.Replay.DurationSeconds -gt $taskReport.LookbackSeconds + 1.1)) { throw 'The replay duration is outside the keyframe allowance.' }
if (Get-ChildItem -LiteralPath $taskDirectory -Force -Filter '*.partial.mp4') { throw 'A partial export file remains.' }
$taskResults = [Collections.Generic.List[object]]::new()
foreach ($taskName in @('Partial','Replay','Repeated','StopExport')) {
    $taskClip = $taskReport.$taskName
    if ($taskClip.EndSeconds -gt $taskClip.TriggerSeconds + 0.0001) { throw 'A clip contains video after its trigger cutoff.' }
    $taskJson = & (Join-Path $taskBin 'ffprobe.exe') -v error -count_frames -show_streams -show_format -of json $taskClip.File
    if ($LASTEXITCODE -ne 0) { throw 'ffprobe could not read a replay.' }
    $taskText = $taskJson -join [Environment]::NewLine
    $taskText | Set-Content -LiteralPath (Join-Path $taskDirectory ($taskName + '-probe.json'))
    $taskMetadata = $taskText | ConvertFrom-Json
    $taskVideo = $taskMetadata.streams | Where-Object codec_type -eq 'video'
    $taskAudio = $taskMetadata.streams | Where-Object codec_type -eq 'audio'
    $taskDuration = [double]::Parse($taskVideo.duration, [Globalization.CultureInfo]::InvariantCulture)
    if ([Math]::Abs([double]::Parse($taskVideo.start_time, [Globalization.CultureInfo]::InvariantCulture)) -gt 0.001) { throw 'Replay video was not rebased to zero.' }
    if ($taskVideo.codec_name -ne 'h264' -or [int]$taskVideo.nb_read_frames -ne $taskClip.VideoFrames) { throw "Incorrect decoded video frame count in $taskName." }
    if ([Math]::Abs($taskDuration - $taskClip.DurationSeconds) -gt 0.04) { throw "Incorrect duration in $taskName." }
    if ($taskReport.IncludeAudio) {
        if ($taskAudio.codec_name -ne 'aac') { throw 'Missing replay AAC track.' }
        $taskAudioDuration = [double]::Parse($taskAudio.duration, [Globalization.CultureInfo]::InvariantCulture)
        if ([Math]::Abs($taskAudioDuration - $taskDuration) -gt 0.05) { throw "Audio duration mismatch in $taskName." }
    } elseif ($taskAudio) { throw 'Unexpected audio track.' }
    & (Join-Path $taskBin 'ffmpeg.exe') -v error -xerror -threads 1 -i $taskClip.File -f null -
    if ($LASTEXITCODE -ne 0) { throw "Replay decode failed: $taskName" }
    $taskRaw = Join-Path $taskDirectory ($taskName + '.rgb')
    & (Join-Path $taskBin 'ffmpeg.exe') -v error -threads 1 -i $taskClip.File -map 0:v:0 -vf 'scale=16:16,format=rgb24' -f rawvideo -y $taskRaw
    if ($LASTEXITCODE -ne 0) { throw 'Could not analyze the replay video.' }
    $taskPixels = [IO.File]::ReadAllBytes($taskRaw)
    $taskWhite = [Collections.Generic.List[double]]::new()
    $taskPink = [Collections.Generic.List[double]]::new()
    $taskPreviousWhite = $false; $taskPreviousPink = $false
    for ($taskFrame = 0; $taskFrame -lt $taskPixels.Length / 768; $taskFrame++) {
        $taskR = 0; $taskG = 0; $taskB = 0
        for ($taskPixel = 0; $taskPixel -lt 256; $taskPixel++) {
            $taskIndex = $taskFrame * 768 + $taskPixel * 3
            $taskR += $taskPixels[$taskIndex]; $taskG += $taskPixels[$taskIndex + 1]; $taskB += $taskPixels[$taskIndex + 2]
        }
        $taskIsWhite = $taskR / 256 -gt 180 -and $taskG / 256 -gt 180 -and $taskB / 256 -gt 180
        $taskIsPink = $taskR / 256 -gt 180 -and $taskG / 256 -lt 120 -and $taskB / 256 -gt 180
        if ($taskIsWhite -and -not $taskPreviousWhite) { $taskWhite.Add($taskFrame / 30.0) }
        if ($taskIsPink -and -not $taskPreviousPink) { $taskPink.Add($taskFrame / 30.0) }
        $taskPreviousWhite = $taskIsWhite; $taskPreviousPink = $taskIsPink
    }
    if ($taskName -eq 'Partial' -and ($taskWhite.Count -ne 0 -or $taskPink.Count -ne 0)) { throw 'Partial history included a future event.' }
    if ($taskName -eq 'Replay' -and ($taskWhite.Count -ne 1 -or $taskPink.Count -ne 0)) { throw 'Pre-trigger replay included an evicted/future event or lost its preceding event.' }
    if ($taskName -in @('Repeated','StopExport') -and ($taskWhite.Count -ne 0 -or $taskPink.Count -ne 1)) { throw 'Later footage was not captured after the first save.' }
    $taskToneOnsets = [Collections.Generic.List[double]]::new()
    $taskAudioOffset = $null
    if ($taskName -eq 'Replay' -and $taskReport.IncludeAudio) {
        $taskPcmPath = Join-Path $taskDirectory 'Replay.f32'
        & (Join-Path $taskBin 'ffmpeg.exe') -v error -threads 1 -i $taskClip.File -map 0:a:0 -ac 1 -ar 48000 -f f32le -y $taskPcmPath
        if ($LASTEXITCODE -ne 0) { throw 'Replay audio analysis failed.' }
        $taskPcm = [IO.File]::ReadAllBytes($taskPcmPath)
        $taskCoefficient = 2 * [Math]::Cos(2 * [Math]::PI * 600 / 48000)
        $taskPreviousTone = $false
        for ($taskBlock = 0; $taskBlock -lt [Math]::Floor($taskPcm.Length / 4 / 480); $taskBlock++) {
            $taskS1 = 0.0; $taskS2 = 0.0
            for ($taskSample = 0; $taskSample -lt 480; $taskSample++) {
                $taskS0 = [BitConverter]::ToSingle($taskPcm, ($taskBlock * 480 + $taskSample) * 4) + $taskCoefficient * $taskS1 - $taskS2
                $taskS2 = $taskS1; $taskS1 = $taskS0
            }
            $taskAmplitude = 2 * [Math]::Sqrt([Math]::Max(0, $taskS1 * $taskS1 + $taskS2 * $taskS2 - $taskCoefficient * $taskS1 * $taskS2)) / 480
            $taskTone = $taskAmplitude -gt 0.003
            if ($taskTone -and -not $taskPreviousTone) { $taskToneOnsets.Add($taskBlock / 100.0) }
            $taskPreviousTone = $taskTone
        }
        if ($taskToneOnsets.Count -ne 1) { throw 'Replay did not preserve its single pre-trigger test tone.' }
        $taskAudioOffset = ($taskToneOnsets[0] - $taskWhite[0]) * 1000
        if ([Math]::Abs($taskAudioOffset) -gt 150) { throw "Replay A/V offset exceeded 150 ms: $taskAudioOffset" }
    }
    $taskResults.Add([ordered]@{ Clip=$taskName; DecodePassed=$true; Frames=[int]$taskVideo.nb_read_frames; Seconds=$taskDuration; WhitePulseSeconds=@($taskWhite); AfterSavePulseSeconds=@($taskPink); AudioToneSeconds=@($taskToneOnsets); AudioMinusVideoMilliseconds=$taskAudioOffset })
}
$taskResult = [ordered]@{ PreTriggerContentPassed=$true; CaptureContinued=$true; SnapshotSurvivedEviction=$true; ErrorRecoveryPassed=$true; Clips=@($taskResults) }
$taskVerification = $taskResult | ConvertTo-Json -Depth 6
$taskVerification | Set-Content -LiteralPath (Join-Path $taskDirectory 'verification.json')
$taskVerification
