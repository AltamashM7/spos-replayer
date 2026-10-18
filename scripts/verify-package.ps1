param(
    [Parameter(Mandatory=$true,ParameterSetName='Directory')][string]$PackageDirectory,
    [Parameter(Mandatory=$true,ParameterSetName='Archive')][string]$ArchivePath,
    [switch]$IntegrityOnly
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if ($ArchivePath) {
    $taskArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
    $taskExpectedZipHash = (Get-Content -LiteralPath ($taskArchive + '.sha256') -Raw).Substring(0,64)
    if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskExpectedZipHash) { throw 'Outer ZIP checksum mismatch.' }
    $taskExtraction = Join-Path $taskRoot ('artifacts/package-extraction-' + [Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory($taskArchive,$taskExtraction)
    $PackageDirectory = Join-Path $taskExtraction 'Rewind'
}
$taskBundle = (Resolve-Path -LiteralPath $PackageDirectory).Path
$taskPrefix = $taskBundle.TrimEnd('\') + '\'
$taskVerified = 0
$taskListed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($taskLine in (Get-Content -LiteralPath (Join-Path $taskBundle 'SHA256SUMS.txt'))) {
    if ($taskLine.Length -lt 67 -or $taskLine.Substring(0,64) -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Malformed package checksum entry.' }
    $taskRelative = $taskLine.Substring(66)
    $taskFile = [IO.Path]::GetFullPath((Join-Path $taskBundle $taskRelative))
    if (-not $taskFile.StartsWith($taskPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Checksum entry leaves the package directory.' }
    if (-not $taskListed.Add($taskFile)) { throw 'Duplicate package checksum entry.' }
    if ((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ne $taskLine.Substring(0,64)) { throw "Package checksum mismatch: $taskRelative" }
    $taskVerified++
}
if ((Get-ChildItem -LiteralPath $taskBundle -File -Recurse).Count -ne $taskVerified + 1) { throw 'Package contains files outside its checksum inventory.' }
$taskRuntimeConfig = Get-Content -LiteralPath (Join-Path $taskBundle 'Rewind.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $taskRuntimeConfig.runtimeOptions.includedFrameworks -or $taskRuntimeConfig.runtimeOptions.framework -or $taskRuntimeConfig.runtimeOptions.frameworks) {
    throw 'The package requires an external .NET runtime.'
}
if ($IntegrityOnly) {
    [pscustomobject]@{ Passed=$true; Verification='File integrity and self-contained metadata'; VerifiedFiles=$taskVerified; PackageDirectory=$taskBundle; Archive=$ArchivePath } | ConvertTo-Json
    return
}
$taskIdentity = [Guid]::NewGuid().ToString('N')
$taskIsolatedRoot = Join-Path ([IO.Path]::GetTempPath()) ('Rewind-portable-' + $taskIdentity)
$taskIsolated = Join-Path $taskIsolatedRoot 'Rewind'
$taskOutput = Join-Path $taskRoot ('artifacts/package-proof-' + $taskIdentity)
New-Item -ItemType Directory -Path $taskIsolatedRoot,$taskOutput -Force | Out-Null
Copy-Item -LiteralPath $taskBundle -Destination $taskIsolated -Recurse
$taskSavedEnvironment = @{}
$taskEnvironmentNames = @('PATH','REWIND_FFMPEG_PATH','DOTNET_MULTILEVEL_LOOKUP','DOTNET_ROLL_FORWARD') +
    @(Get-ChildItem Env: | Where-Object Name -Like 'DOTNET_ROOT*' | Select-Object -ExpandProperty Name)
foreach ($taskName in $taskEnvironmentNames) { $taskSavedEnvironment[$taskName] = [Environment]::GetEnvironmentVariable($taskName) }
function Invoke-PackagedDiagnostic([string]$Mode,[string]$OutputName) {
    $taskDiagnostic = Join-Path $taskOutput $OutputName
    New-Item -ItemType Directory -Path $taskDiagnostic -Force | Out-Null
    $taskProcess = Start-Process -FilePath (Join-Path $taskIsolated 'Rewind.exe') -ArgumentList @(
        $Mode, ('"' + $taskDiagnostic + '"')
    ) -WorkingDirectory $taskIsolatedRoot -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskDiagnostic 'stderr.txt')
    if (-not $taskProcess.WaitForExit(45000)) { $taskProcess.Kill(); throw "Packaged diagnostic timed out: $taskDiagnostic" }
    if ($taskProcess.ExitCode -ne 0) {
        Get-ChildItem -LiteralPath $taskDiagnostic -Filter '*error.txt' | ForEach-Object { Get-Content -LiteralPath $_.FullName }
        throw "Packaged diagnostic failed: $taskDiagnostic"
    }
}
try {
    foreach ($taskName in $taskEnvironmentNames) { Remove-Item -LiteralPath ('Env:' + $taskName) -ErrorAction SilentlyContinue }
    [Environment]::SetEnvironmentVariable('PATH',($env:SystemRoot + ';' + (Join-Path $env:SystemRoot 'System32')))
    [Environment]::SetEnvironmentVariable('DOTNET_MULTILEVEL_LOOKUP','0')
    [Environment]::SetEnvironmentVariable('DOTNET_ROLL_FORWARD','Disable')
    Invoke-PackagedDiagnostic '--smoke-test' 'startup'
    # An empty override must behave like an absent one, rather than suppressing bundle discovery.
    [Environment]::SetEnvironmentVariable('REWIND_FFMPEG_PATH','')
    Invoke-PackagedDiagnostic '--package-proof' 'media'
}
finally {
    foreach ($taskName in $taskEnvironmentNames) {
        if ($null -eq $taskSavedEnvironment[$taskName]) { Remove-Item -LiteralPath ('Env:' + $taskName) -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($taskName,$taskSavedEnvironment[$taskName]) }
    }
}
$taskStartup = Get-Content -LiteralPath (Join-Path $taskOutput 'startup/startup.json') -Raw | ConvertFrom-Json
if (-not $taskStartup.WindowRendered -or -not $taskStartup.CaptureSupported) { throw 'Packaged WPF startup/capture capability failed.' }
$taskProof = Get-Content -LiteralPath (Join-Path $taskOutput 'media/package-proof.json') -Raw | ConvertFrom-Json
$taskIsolatedPrefix = $taskIsolated.TrimEnd('\') + '\'
foreach ($taskPath in @($taskProof.ApplicationDirectory,$taskProof.RuntimeDirectory,$taskProof.CoreLibrary)) {
    if (-not $taskPath.StartsWith($taskIsolatedPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Packaged app used a runtime outside its isolated bundle.' }
}
$taskNativePrefix = (Join-Path $taskIsolated 'native/ffmpeg').TrimEnd('\') + '\'
foreach ($taskModule in $taskProof.NativeModules) {
    if (-not $taskModule.FileName.StartsWith($taskNativePrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Native module was loaded outside the bundle.' }
}
foreach ($taskModuleName in @('avcodec-63.dll','avformat-63.dll','avutil-61.dll','swscale-10.dll')) {
    if ($taskModuleName -notin $taskProof.NativeModules.ModuleName) { throw "Packaged codec module was not exercised: $taskModuleName" }
}
if ($taskProof.Replay.VideoFrames -lt 60) { throw 'The packaged app did not export enough captured history.' }
$taskFfmpeg = Join-Path $taskRoot '.tools/ffmpeg/ffmpeg-9.0.2-full_build-shared/bin/ffmpeg.exe'
$taskFfprobe = Join-Path $taskRoot '.tools/ffmpeg/ffmpeg-9.0.2-full_build-shared/bin/ffprobe.exe'
& $taskFfmpeg -v error -xerror -threads 1 -i $taskProof.Replay.File -f null -
if ($LASTEXITCODE -ne 0) { throw 'The packaged export did not fully decode.' }
$taskProbeText = (& $taskFfprobe -v error -show_streams -show_format -of json $taskProof.Replay.File) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) { throw 'The packaged export could not be inspected.' }
$taskProbeText | Set-Content -LiteralPath (Join-Path $taskOutput 'media/probe.json')
$taskProbe = $taskProbeText | ConvertFrom-Json
$taskVideo = $taskProbe.streams | Where-Object codec_type -eq video
$taskAudio = $taskProbe.streams | Where-Object codec_type -eq audio
if ($taskVideo.codec_name -ne 'h264' -or $taskAudio.codec_name -ne 'aac') { throw 'Packaged export is missing H.264 or AAC.' }
$taskVideoDuration = [double]::Parse($taskVideo.duration,[Globalization.CultureInfo]::InvariantCulture)
$taskAudioDuration = [double]::Parse($taskAudio.duration,[Globalization.CultureInfo]::InvariantCulture)
if ([Math]::Abs($taskVideoDuration - $taskAudioDuration) -gt 0.05) { throw 'Packaged video/audio duration mismatch.' }
if (Get-ChildItem -LiteralPath (Join-Path $taskOutput 'media') -Filter '*.partial.mp4' -Force) { throw 'Packaged export left a partial file.' }
[pscustomobject]@{
    Passed=$true; VerifiedFiles=$taskVerified; IsolatedPackage=$taskIsolated; Diagnostics=$taskOutput
    Runtime=$taskProof.Runtime; NativeVersion=$taskProof.NativeVersion; VideoFrames=$taskProof.Replay.VideoFrames
    VideoSeconds=$taskVideoDuration; AudioSeconds=$taskAudioDuration; FullDecodePassed=$true
} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $taskOutput 'verification.json')
