$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'dotnet.ps1') run --project src/Rewind.App/Rewind.App.csproj --no-build --no-restore --no-launch-profile
exit $LASTEXITCODE
