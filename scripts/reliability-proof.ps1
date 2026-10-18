param([ValidateRange(0,60)][int]$Minutes = 0, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$taskOutput = Join-Path $taskRoot ('artifacts\reliability-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskAssembly = Join-Path $taskRoot 'src\Rewind.App\bin\Debug\net10.0-windows10.0.19041.0\Rewind.dll'
$taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools\dotnet\dotnet.exe') -ArgumentList @(
    ('"' + $taskAssembly + '"'), '--reliability-proof', ('"' + $taskOutput + '"'), [string]($Minutes * 60)
) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput 'stderr.txt')
Write-Output "Reliability diagnostics: $taskOutput"
$taskDeadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(90, $Minutes * 60 + 90))
while (-not $taskProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $taskDeadline) { $taskProcess.Kill(); throw "Reliability fixture timed out: $taskOutput" }
}
if ($taskProcess.ExitCode -ne 0) {
    Get-Content (Join-Path $taskOutput 'reliability-error.txt') -ErrorAction SilentlyContinue
    throw "Reliability fixture failed: $taskOutput"
}
$taskReportPath = Join-Path $taskOutput 'reliability.json'
if (-not (Test-Path -LiteralPath $taskReportPath)) { throw 'Reliability report missing.' }
Get-Content -LiteralPath $taskReportPath
$taskBin = Join-Path $taskRoot '.tools\ffmpeg\ffmpeg-9.0.2-full_build-shared\bin'
foreach ($taskClip in (Get-ChildItem -LiteralPath $taskOutput -Filter '*.mp4' | Where-Object Length -gt 0)) {
    & (Join-Path $taskBin 'ffmpeg.exe') -v error -xerror -threads 1 -i $taskClip.FullName -f null -
    if ($LASTEXITCODE -ne 0) { throw "Full decoding failed: $($taskClip.FullName)" }
    $taskProbe = & (Join-Path $taskBin 'ffprobe.exe') -v error -show_streams -show_format -of json $taskClip.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the reliability export.' }
    $taskProbe | Set-Content -LiteralPath ($taskClip.FullName + '.json')
}
Write-Output 'Reliability exports fully decoded.'
