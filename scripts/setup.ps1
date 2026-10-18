$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$taskTools = Join-Path $taskRoot '.tools'
$taskInstaller = Join-Path $taskTools 'dotnet-install.ps1'
$taskDotnet = Join-Path $taskTools 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath (Join-Path $taskTools "dotnet\sdk\$taskVersion")) {
    & (Join-Path $PSScriptRoot 'dotnet.ps1') --version
    exit $LASTEXITCODE
}
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $taskInstaller
& $taskInstaller -Version $taskVersion -Architecture x64 -InstallDir (Join-Path $taskTools 'dotnet') -NoPath
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'SDK installation failed.' }
& (Join-Path $PSScriptRoot 'dotnet.ps1') --version
