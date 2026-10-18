param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskApp = 'src/Rewind.App/Rewind.App.csproj'
$taskPublish = Join-Path $taskRoot 'artifacts/publish/win-x64'
$taskBuildInputs = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -File -Recurse | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]')
foreach ($taskInput in @('Directory.Build.props','global.json','NuGet.Config','native-dependencies.json')) {
    $taskBuildInputs += Get-Item -LiteralPath (Join-Path $taskRoot $taskInput)
}
$taskFingerprint = (($taskBuildInputs | Sort-Object FullName | ForEach-Object {
    [IO.Path]::GetRelativePath($taskRoot,$_.FullName) + '=' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}) -join "`n")
$taskFingerprintPath = Join-Path $taskPublish 'publish-inputs.txt'
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'dotnet.ps1') restore $taskApp -r win-x64 --locked-mode '-p:SelfContained=true'
    if ($LASTEXITCODE -ne 0) { throw 'Portable dependency restore failed.' }
    & (Join-Path $PSScriptRoot 'dotnet.ps1') publish $taskApp -c Release -r win-x64 --self-contained true --no-restore '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' -o $taskPublish
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
    [IO.File]::WriteAllText($taskFingerprintPath,$taskFingerprint)
}
elseif (-not (Test-Path -LiteralPath $taskFingerprintPath) -or [IO.File]::ReadAllText($taskFingerprintPath) -ne $taskFingerprint) {
    throw 'Published inputs have changed or are unverified. Run package.ps1 without -SkipPublish.'
}
foreach ($taskRequired in @('Rewind.exe','Rewind.runtimeconfig.json','coreclr.dll','System.Private.CoreLib.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskPublish $taskRequired))) { throw "Self-contained publish is missing $taskRequired." }
}
$taskIdentity = 'Rewind-win-x64-preview-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$taskStage = Join-Path $taskRoot ('artifacts/packages/' + $taskIdentity)
$taskBundle = Join-Path $taskStage 'Rewind'
New-Item -ItemType Directory -Path $taskBundle -Force | Out-Null
Copy-Item -Path (Join-Path $taskPublish '*') -Destination $taskBundle -Recurse

$taskNative = (Get-Content -LiteralPath (Join-Path $taskRoot 'native-dependencies.json') -Raw | ConvertFrom-Json).ffmpeg
$taskNativeRoot = Join-Path $taskRoot ('.tools/ffmpeg/' + $taskNative.directory)
$taskNativeOut = Join-Path $taskBundle 'native/ffmpeg'
$taskNotices = Join-Path $taskBundle 'notices'
New-Item -ItemType Directory -Path $taskNativeOut, (Join-Path $taskNotices 'ffmpeg'), (Join-Path $taskNotices 'packages'), (Join-Path $taskNotices 'runtime') -Force | Out-Null
foreach ($taskDll in @('avcodec-63.dll','avformat-63.dll','avutil-61.dll','swscale-10.dll','swresample-7.dll')) {
    Copy-Item -LiteralPath (Join-Path $taskNativeRoot ('bin/' + $taskDll)) -Destination $taskNativeOut
}
Copy-Item -LiteralPath (Join-Path $taskNativeRoot 'LICENSE'), (Join-Path $taskNativeRoot 'README.txt') -Destination (Join-Path $taskNotices 'ffmpeg')
Copy-Item -LiteralPath (Join-Path $taskRoot 'native-dependencies.json') -Destination (Join-Path $taskNotices 'ffmpeg')

$taskLock = Get-Content -LiteralPath (Join-Path $taskRoot 'src/Rewind.App/packages.lock.json') -Raw | ConvertFrom-Json
$taskPackages = @{}
foreach ($taskFramework in $taskLock.dependencies.PSObject.Properties) {
    foreach ($taskEntry in $taskFramework.Value.PSObject.Properties) {
        if ($taskEntry.Value.type -eq 'Project') { continue }
        $taskId = $taskEntry.Name.ToLowerInvariant()
        $taskVersion = $taskEntry.Value.resolved
        if (-not $taskVersion) { throw "Missing locked version for $taskId." }
        $taskKey = $taskId + '/' + $taskVersion
        if ($taskPackages.ContainsKey($taskKey)) { continue }
        $taskPackageRoot = Join-Path $taskRoot ('.tools/nuget-packages/' + $taskKey)
        [xml]$taskSpec = Get-Content -LiteralPath (Join-Path $taskPackageRoot ($taskId + '.nuspec')) -Raw
        $taskMetadata = $taskSpec.package.metadata
        Copy-Item -LiteralPath (Join-Path $taskPackageRoot ($taskId + '.' + $taskVersion + '.nupkg')) -Destination (Join-Path $taskNotices 'packages')
        $taskPackages[$taskKey] = [pscustomobject]@{ Id=$taskEntry.Name; Version=$taskVersion; Authors=[string]$taskMetadata.authors; LicenseType=[string]$taskMetadata.license.type; License=[string]$taskMetadata.license.InnerText; Copyright=[string]$taskMetadata.copyright; ProjectUrl=[string]$taskMetadata.projectUrl; ContentHash=$taskEntry.Value.contentHash }
    }
}
$taskPackages.Values | Sort-Object Id | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskNotices 'packages.json') -Encoding utf8
$taskRuntimeConfig = Get-Content -LiteralPath (Join-Path $taskBundle 'Rewind.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($taskRuntime in $taskRuntimeConfig.runtimeOptions.includedFrameworks) {
    $taskRuntimeId = if ($taskRuntime.name -eq 'Microsoft.NETCore.App') { 'microsoft.netcore.app.runtime.win-x64' } elseif ($taskRuntime.name -eq 'Microsoft.WindowsDesktop.App') { 'microsoft.windowsdesktop.app.runtime.win-x64' } else { throw 'Unexpected bundled runtime.' }
    $taskRuntimeRoot = Join-Path $taskRoot ('.tools/nuget-packages/' + $taskRuntimeId + '/' + $taskRuntime.version)
    $taskRuntimeNotice = Join-Path $taskNotices ('runtime/' + $taskRuntimeId)
    New-Item -ItemType Directory -Path $taskRuntimeNotice -Force | Out-Null
    $taskLicenseFiles = Get-ChildItem -LiteralPath $taskRuntimeRoot -File -Recurse | Where-Object Name -Match '^(LICENSE|THIRD.?PARTY.?NOTICES)(\.[^.]+)?$'
    if (-not $taskLicenseFiles) { throw "Missing original runtime notices for $taskRuntimeId." }
    foreach ($taskNotice in $taskLicenseFiles) { Copy-Item -LiteralPath $taskNotice.FullName -Destination $taskRuntimeNotice }
    Copy-Item -LiteralPath (Join-Path $taskRuntimeRoot ($taskRuntimeId + '.nuspec')) -Destination $taskRuntimeNotice
}

Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/PORTABLE-README.txt') -Destination (Join-Path $taskBundle 'README.txt')
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs') -Destination $taskBundle -Recurse
$taskSourceRoot = Join-Path $taskRoot ('artifacts/package-source/' + $taskIdentity)
New-Item -ItemType Directory -Path $taskSourceRoot -Force | Out-Null
$taskSourceFiles = @()
foreach ($taskFolder in @('src','tests','scripts','docs')) {
    $taskSourceFiles += Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskFolder) -File -Recurse | Where-Object FullName -NotMatch '[\\/](bin|obj|TestResults)[\\/]'
}
foreach ($taskRootFile in @('.gitignore','Directory.Build.props','global.json','NuGet.Config','Rewind.slnx','native-dependencies.json','README.md')) {
    $taskSourceFiles += Get-Item -LiteralPath (Join-Path $taskRoot $taskRootFile) -Force
}
$taskSourceManifest = foreach ($taskFile in ($taskSourceFiles | Sort-Object FullName)) {
    $taskRelative = [IO.Path]::GetRelativePath($taskRoot, $taskFile.FullName)
    $taskSourceCopy = Join-Path $taskSourceRoot $taskRelative
    New-Item -ItemType Directory -Path (Split-Path $taskSourceCopy -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskSourceCopy
    [pscustomobject]@{ Path=$taskRelative.Replace('\','/'); Sha256=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash }
}
New-Item -ItemType Directory -Path (Join-Path $taskBundle 'source') -Force | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($taskSourceRoot, (Join-Path $taskBundle 'source/Rewind-source.zip'))
$taskManifest = [ordered]@{
    Name='Rewind'; Kind='Local preview'; RuntimeIdentifier='win-x64'; SelfContained=$true
    SdkVersion=(Get-Content -LiteralPath (Join-Path $taskRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    Runtimes=$taskRuntimeConfig.runtimeOptions.includedFrameworks
    Ffmpeg=$taskNative; ManualChecks='Pending'; PublicRelease='Pending source/notices preparation and repository-license decision'
    SourceFiles=$taskSourceManifest
}
$taskManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskBundle 'package-manifest.json') -Encoding utf8
$taskSums = foreach ($taskFile in (Get-ChildItem -LiteralPath $taskBundle -File -Recurse | Sort-Object FullName)) {
    (Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetRelativePath($taskBundle,$taskFile.FullName).Replace('\','/')
}
$taskSums | Set-Content -LiteralPath (Join-Path $taskBundle 'SHA256SUMS.txt') -Encoding utf8
$taskZip = $taskStage + '.zip'
Write-Output 'Creating the portable ZIP...'
[IO.Compression.ZipFile]::CreateFromDirectory($taskStage, $taskZip, [IO.Compression.CompressionLevel]::Optimal, $false)
$taskZipHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash
($taskZipHash + '  ' + [IO.Path]::GetFileName($taskZip)) | Set-Content -LiteralPath ($taskZip + '.sha256') -Encoding ascii
[pscustomobject]@{ PackageDirectory=$taskBundle; Zip=$taskZip; Sha256=$taskZipHash; ZipMiB=[Math]::Round((Get-Item -LiteralPath $taskZip).Length / 1MB,2) } | ConvertTo-Json
