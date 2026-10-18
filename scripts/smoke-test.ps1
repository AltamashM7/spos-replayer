$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$taskOutput = Join-Path $taskRoot ('artifacts\startup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskAssembly = Join-Path $taskRoot 'src\Rewind.App\bin\Debug\net10.0-windows10.0.19041.0\Rewind.dll'
$taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools\dotnet\dotnet.exe') -ArgumentList @(
    ('"' + $taskAssembly + '"'), '--smoke-test', ('"' + $taskOutput + '"')
) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput 'stderr.txt')
if (-not $taskProcess.WaitForExit(30000)) {
    $taskProcess.Kill()
    throw "Startup smoke check timed out. Diagnostics: $taskOutput"
}
if ($taskProcess.ExitCode -ne 0) { throw "App failed to start. Diagnostics: $taskOutput" }
$taskResultPath = Join-Path $taskOutput 'startup.json'
if (-not (Test-Path -LiteralPath $taskResultPath)) { throw 'App exited without a startup report.' }
$taskResult = Get-Content -LiteralPath $taskResultPath -Raw | ConvertFrom-Json
if (-not $taskResult.WindowRendered) { throw 'The WPF window did not render.' }
Get-Content -LiteralPath $taskResultPath
Write-Output "Window image: $(Join-Path $taskOutput 'startup.png')"
