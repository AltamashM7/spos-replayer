param([switch]$VideoOnly, [switch]$Cancel, [switch]$Silence)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$taskOutput = Join-Path $taskRoot ('artifacts\media-proof-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskAssembly = Join-Path $taskRoot 'src\Rewind.App\bin\Debug\net10.0-windows10.0.19041.0\Rewind.dll'
$taskArguments = @(('"' + $taskAssembly + '"'), '--media-proof', ('"' + $taskOutput + '"'))
if ($VideoOnly) { $taskArguments += '--video-only' }
if ($Cancel) { $taskArguments += '--cancel' }
if ($Silence) { $taskArguments += '--silence' }
$taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools\dotnet\dotnet.exe') -ArgumentList $taskArguments `
    -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput 'stderr.txt')
if (-not $taskProcess.WaitForExit(45000)) { $taskProcess.Kill(); throw "Media proof timed out: $taskOutput" }
if ($taskProcess.ExitCode -ne 0) {
    Get-Content (Join-Path $taskOutput 'recording-error.txt'), (Join-Path $taskOutput 'stderr.txt') -ErrorAction SilentlyContinue
    throw "Media proof failed: $taskOutput"
}
if ($Cancel) {
    if (-not (Test-Path (Join-Path $taskOutput 'cancelled.json'))) { throw 'Cancellation was not observed.' }
    if (Get-ChildItem -LiteralPath $taskOutput -Force -Filter '*.mp4') { throw 'Cancellation left a video file behind.' }
    Write-Output "Cancellation passed: $taskOutput"
} else {
    Get-Content (Join-Path $taskOutput 'recording.json')
    & (Join-Path $PSScriptRoot 'verify-media.ps1') -Directory $taskOutput -VideoOnly:$VideoOnly -Silence:$Silence
    Write-Output "Clip: $(Join-Path $taskOutput 'sample.mp4')"
}
