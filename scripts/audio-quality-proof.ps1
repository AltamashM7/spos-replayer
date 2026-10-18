param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$taskOutput = Join-Path $taskRoot ('artifacts/audio-quality-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskAssembly = Join-Path $taskRoot 'src/Rewind.App/bin/Debug/net10.0-windows10.0.19041.0/Rewind.dll'
$taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools/dotnet/dotnet.exe') -ArgumentList @(
    ('"' + $taskAssembly + '"'), '--audio-quality-proof', ('"' + $taskOutput + '"')
) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput 'stderr.txt')
if (-not $taskProcess.WaitForExit(45000)) { $taskProcess.Kill(); throw "Audio-quality fixture timed out: $taskOutput" }
if ($taskProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $taskOutput 'audio-quality-error.txt') -ErrorAction SilentlyContinue
    throw "Audio-quality fixture failed: $taskOutput"
}
$taskReport = Get-Content -LiteralPath (Join-Path $taskOutput 'audio-quality.json') -Raw | ConvertFrom-Json
$taskReport | Select-Object Packets,JitterAdjustedPackets,TimestampDeltas,DeviceDeltas,Discontinuities,Peak,OverFullScaleSamples,ManagedPauseMilliseconds | ConvertTo-Json -Depth 6
Write-Output "Audio quality evidence: $taskOutput"
