param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$taskOutput = Join-Path $taskRoot ('artifacts/monitor-proof-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskAssembly = Join-Path $taskRoot 'src/Rewind.App/bin/Debug/net10.0-windows10.0.19041.0/Rewind.dll'
$taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools/dotnet/dotnet.exe') -ArgumentList @(
    ('"' + $taskAssembly + '"'), '--monitor-proof', ('"' + $taskOutput + '"')
) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput 'stderr.txt')
if (-not $taskProcess.WaitForExit(45000)) { $taskProcess.Kill(); throw "Monitor fixture timed out: $taskOutput" }
if ($taskProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $taskOutput 'monitor-error.txt') -ErrorAction SilentlyContinue
    throw "Monitor fixture failed: $taskOutput"
}
Get-Content -LiteralPath (Join-Path $taskOutput 'monitor.json')
Write-Output "Monitor evidence: $taskOutput"
