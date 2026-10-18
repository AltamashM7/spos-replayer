param([switch]$VideoOnly, [switch]$DefaultDuration)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$taskOutput = Join-Path $taskRoot ('artifacts\replay-proof-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskAssembly = Join-Path $taskRoot 'src\Rewind.App\bin\Debug\net10.0-windows10.0.19041.0\Rewind.dll'
$taskArguments = @(('"' + $taskAssembly + '"'), '--replay-proof', ('"' + $taskOutput + '"'))
if ($VideoOnly) { $taskArguments += '--video-only' }
if ($DefaultDuration) { $taskArguments += '--default-duration' }
$taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools\dotnet\dotnet.exe') -ArgumentList $taskArguments `
    -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput 'stderr.txt')
if (-not $taskProcess.WaitForExit(60000)) { $taskProcess.Kill(); throw "Replay proof timed out: $taskOutput" }
if ($taskProcess.ExitCode -ne 0) {
    Get-Content (Join-Path $taskOutput 'replay-error.txt'), (Join-Path $taskOutput 'stderr.txt') -ErrorAction SilentlyContinue
    throw "Replay proof failed: $taskOutput"
}
Get-Content (Join-Path $taskOutput 'replay.json')
& (Join-Path $PSScriptRoot 'verify-replay.ps1') -Directory $taskOutput
Write-Output "Replay proof: $taskOutput"
