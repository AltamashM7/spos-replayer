param([switch]$SkipBuild, [switch]$Layout)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$taskOutput = Join-Path $taskRoot ('artifacts\desktop-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskAssembly = Join-Path $taskRoot 'src\Rewind.App\bin\Debug\net10.0-windows10.0.19041.0\Rewind.dll'
$taskMode = if ($Layout) { '--layout-proof' } else { '--desktop-proof' }
$taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools\dotnet\dotnet.exe') -ArgumentList @(
    ('"' + $taskAssembly + '"'), $taskMode, ('"' + $taskOutput + '"')
) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput 'stderr.txt')
if (-not $taskProcess.WaitForExit(30000)) {
    $taskProcess.Kill()
    throw "Desktop check timed out. Diagnostics: $taskOutput"
}
if ($taskProcess.ExitCode -ne 0) { throw "Desktop checks failed. Diagnostics: $taskOutput" }
$taskReport = Join-Path $taskOutput $(if ($Layout) { 'layout.json' } else { 'desktop.json' })
if (-not (Test-Path -LiteralPath $taskReport)) { throw "Desktop report missing: $taskOutput" }
Get-Content -LiteralPath $taskReport
Write-Output "Desktop artifacts: $taskOutput"
