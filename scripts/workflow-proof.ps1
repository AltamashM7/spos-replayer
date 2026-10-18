param([switch]$SkipBuild, [string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$taskOutput = Join-Path $taskRoot ('artifacts\workflow-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskAssembly = if ($AssemblyPath) { (Resolve-Path -LiteralPath $AssemblyPath).Path } else {
    Join-Path $taskRoot 'src\Rewind.App\bin\Debug\net10.0-windows10.0.19041.0\Rewind.dll'
}
$taskProcess = Start-Process -FilePath (Join-Path $taskRoot '.tools\dotnet\dotnet.exe') -ArgumentList @(
    ('"' + $taskAssembly + '"'), '--workflow-proof', ('"' + $taskOutput + '"')
) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskOutput 'stderr.txt')
if (-not $taskProcess.WaitForExit(45000)) { $taskProcess.Kill(); throw "Live workflow check timed out: $taskOutput" }
if ($taskProcess.ExitCode -ne 0) {
    Get-Content (Join-Path $taskOutput 'workflow-error.txt') -ErrorAction SilentlyContinue
    throw "Live workflow check failed: $taskOutput"
}
Get-Content -LiteralPath (Join-Path $taskOutput 'workflow.json')
$taskFfmpeg = Join-Path $taskRoot '.tools\ffmpeg\ffmpeg-9.0.2-full_build-shared\bin\ffmpeg.exe'
foreach ($taskClip in (Get-ChildItem -LiteralPath $taskOutput -Filter '*.mp4')) {
    & $taskFfmpeg -v error -xerror -threads 1 -i $taskClip.FullName -f null -
    if ($LASTEXITCODE -ne 0) { throw "Workflow replay did not decode: $($taskClip.FullName)" }
}
Write-Output "Live workflow verified: $taskOutput"
