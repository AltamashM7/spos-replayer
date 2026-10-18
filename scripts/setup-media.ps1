$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDependency = (Get-Content -LiteralPath (Join-Path $taskRoot 'native-dependencies.json') -Raw | ConvertFrom-Json).ffmpeg
$taskTools = Join-Path $taskRoot '.tools'
$taskDestination = Join-Path $taskTools 'ffmpeg'
$taskBinary = Join-Path $taskDestination ($taskDependency.directory + '\bin\avcodec-63.dll')
if (Test-Path -LiteralPath $taskBinary) { Write-Output "FFmpeg $($taskDependency.version) is already installed locally."; exit 0 }
$taskSevenZip = Get-Command 7z -ErrorAction SilentlyContinue
if (-not $taskSevenZip) { throw '7-Zip must be available as 7z on PATH to unpack the native dependencies.' }
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
$taskArchive = Join-Path $taskTools ('ffmpeg-' + $taskDependency.version + '-shared.7z')
& curl.exe --location --fail --retry 2 --silent --show-error $taskDependency.url --output $taskArchive
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg download failed.' }
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskDependency.sha256) {
    throw 'The FFmpeg archive checksum does not match the pinned manifest.'
}
& $taskSevenZip.Source x $taskArchive ('-o' + $taskDestination) -y -bso0 -bsp0
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $taskBinary)) { throw 'FFmpeg extraction failed.' }
Write-Output "FFmpeg $($taskDependency.version) installed locally with its published SHA-256 verified."
