$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'dotnet.ps1') restore Rewind.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'dotnet.ps1') test Rewind.slnx --no-restore
exit $LASTEXITCODE
