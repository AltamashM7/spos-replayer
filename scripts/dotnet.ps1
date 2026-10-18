# Keep SDK state and downloaded packages inside this checkout.
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) {
    throw 'Local SDK missing. Run .\scripts\setup.ps1 first.'
}
$env:DOTNET_ROOT = Split-Path $taskDotnet -Parent
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.tools\nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $taskRoot
try {
    & $taskDotnet @args
    $taskExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}
exit $taskExitCode
