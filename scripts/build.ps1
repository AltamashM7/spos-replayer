param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'dotnet.ps1') restore Rewind.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'dotnet.ps1') build Rewind.slnx --configuration $Configuration --no-restore
exit $LASTEXITCODE
