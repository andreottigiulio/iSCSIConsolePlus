<#
.SYNOPSIS
    Builds iSCSIConsolePlus from source with a portable, self-contained toolchain.

.DESCRIPTION
    On first run the script downloads everything it needs into .tools\ next to
    this file (nothing is installed system-wide, PATH is not modified):

      * the .NET SDK (Windows x64 zip build)
      * ILMerge (NuGet package), used to merge the assemblies into one exe

    It then restores the NuGet packages into .tools\nuget-packages, builds the
    requested target framework, merges the output into a single executable and
    (unless -NoPackage) creates dist\ISCSIConsole_<version>_<tfm>.zip with a
    blank settings file.

.PARAMETER TargetFramework
    Target framework to build. Default: net472 (the only one tested).

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER SdkVersion
    Exact .NET SDK version to use. Default: the latest of -SdkChannel.

.PARAMETER SdkChannel
    .NET release channel used to resolve the SDK. Default: 8.0.

.PARAMETER IlMergeVersion
    ILMerge version to download from NuGet. Default: 3.0.41.

.PARAMETER Clean
    Removes build outputs before building.

.PARAMETER NoPackage
    Skips the creation of the distributable zip.

.EXAMPLE
    powershell -NoProfile -File build.ps1

.EXAMPLE
    powershell -NoProfile -File build.ps1 -Clean -TargetFramework net472
#>
[CmdletBinding()]
param(
    [string]$TargetFramework = 'net472',
    [string]$Configuration = 'Release',
    [string]$SdkChannel = '8.0',
    [string]$SdkVersion = '',
    [string]$IlMergeVersion = '3.0.41',
    [switch]$Clean,
    [switch]$NoPackage
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    Write-Host 'Run this script with Windows PowerShell (powershell.exe), not pwsh.' -ForegroundColor Red
    exit 1
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = $PSScriptRoot
$toolsDir = Join-Path $root '.tools'
$downloadsDir = Join-Path $toolsDir 'downloads'
$dotnetDir = Join-Path $toolsDir 'dotnet'
$ilmergeDir = Join-Path $toolsDir 'ilmerge'
$distDir = Join-Path $root 'dist'
$project = Join-Path $root 'ISCSIConsole\ISCSIConsole.csproj'

function Write-Step([string]$Text) {
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Write-Detail([string]$Text) {
    Write-Host "    $Text" -ForegroundColor DarkGray
}

function Get-FileSha512([string]$Path) {
    return (Get-FileHash -Path $Path -Algorithm SHA512).Hash.ToLowerInvariant()
}

function Get-RemoteFile([string]$Url, [string]$Destination, [string]$Sha512) {
    if ((Test-Path $Destination) -and $Sha512) {
        if ((Get-FileSha512 $Destination) -eq $Sha512.ToLowerInvariant()) {
            Write-Detail "using cached $(Split-Path -Leaf $Destination)"
            return
        }
        Remove-Item $Destination -Force
    }

    Write-Detail "downloading $Url"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null

    $progress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
    }
    finally {
        $ProgressPreference = $progress
    }

    if ($Sha512) {
        $actual = Get-FileSha512 $Destination
        if ($actual -ne $Sha512.ToLowerInvariant()) {
            Remove-Item $Destination -Force
            throw "Checksum mismatch for $Url (expected $Sha512, got $actual)."
        }
    }
}

function Get-SystemTar {
    # The bsdtar shipped with Windows handles zip archives; GNU tar from Git/MSYS does not.
    $path = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (Test-Path $path) {
        return $path
    }
    return $null
}

function Expand-Zip([string]$ZipPath, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $tar = Get-SystemTar
    if ($tar) {
        & $tar -xf $ZipPath -C $Destination | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to extract $ZipPath."
        }
    }
    else {
        Expand-Archive -Path $ZipPath -DestinationPath $Destination -Force
    }
}

function Get-SdkDownload([string]$Channel, [string]$Version) {
    $metadataUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/$Channel/releases.json"
    Write-Detail "reading $metadataUrl"
    $metadata = Invoke-RestMethod -Uri $metadataUrl -UseBasicParsing

    $releases = if ($Version) {
        $metadata.releases | Where-Object { $_.sdk.version -eq $Version }
    }
    else {
        $metadata.releases | Select-Object -First 1
    }

    $release = @($releases) | Select-Object -First 1
    if (-not $release) {
        throw ".NET SDK $Version was not found in channel $Channel."
    }

    $file = $release.sdk.files | Where-Object { $_.name -eq 'dotnet-sdk-win-x64.zip' } | Select-Object -First 1
    if (-not $file) {
        throw "The .NET SDK $($release.sdk.version) has no Windows x64 zip."
    }

    return [pscustomobject]@{
        Version = $release.sdk.version
        Url     = $file.url
        Sha512  = $file.hash
    }
}

function Get-DotNetSdk {
    $dotnetExe = Join-Path $dotnetDir 'dotnet.exe'
    $marker = Join-Path $dotnetDir '.sdk-version'
    $installed = if (Test-Path $marker) { (Get-Content $marker -Raw).Trim() } else { '' }

    if ((Test-Path $dotnetExe) -and $installed -and (-not $SdkVersion -or $installed -eq $SdkVersion)) {
        Write-Step ".NET SDK $installed already available"
        return $dotnetExe
    }

    Write-Step 'Preparing the .NET SDK'
    $sdk = Get-SdkDownload -Channel $SdkChannel -Version $SdkVersion
    $zip = Join-Path $downloadsDir "dotnet-sdk-$($sdk.Version)-win-x64.zip"
    Get-RemoteFile -Url $sdk.Url -Destination $zip -Sha512 $sdk.Sha512

    if (Test-Path $dotnetDir) {
        Remove-Item $dotnetDir -Recurse -Force
    }
    Write-Detail "extracting into $dotnetDir"
    Expand-Zip -ZipPath $zip -Destination $dotnetDir

    if (-not (Test-Path $dotnetExe)) {
        throw "The .NET SDK was extracted but dotnet.exe is missing from $dotnetDir."
    }

    Set-Content -Path $marker -Value $sdk.Version -NoNewline
    Remove-Item $zip -Force
    Write-Detail ".NET SDK $($sdk.Version) ready"
    return $dotnetExe
}

function Get-IlMerge {
    $ilmergeExe = Join-Path $ilmergeDir 'ILMerge.exe'
    $compilerDll = Join-Path $ilmergeDir 'System.Compiler.dll'

    if ((Test-Path $ilmergeExe) -and (Test-Path $compilerDll)) {
        Write-Step "ILMerge $IlMergeVersion already available"
        return $ilmergeExe
    }

    Write-Step "Preparing ILMerge $IlMergeVersion"
    $nupkg = Join-Path $downloadsDir "ilmerge.$IlMergeVersion.nupkg"
    $url = "https://api.nuget.org/v3-flatcontainer/ilmerge/$IlMergeVersion/ilmerge.$IlMergeVersion.nupkg"
    Get-RemoteFile -Url $url -Destination $nupkg

    $extractDir = Join-Path $downloadsDir "ilmerge-$IlMergeVersion"
    if (Test-Path $extractDir) {
        Remove-Item $extractDir -Recurse -Force
    }
    Expand-Zip -ZipPath $nupkg -Destination $extractDir

    $sourceDir = Join-Path $extractDir 'tools\net452'
    if (-not (Test-Path (Join-Path $sourceDir 'ILMerge.exe'))) {
        throw "ILMerge.exe was not found inside the ILMerge $IlMergeVersion package."
    }

    New-Item -ItemType Directory -Force -Path $ilmergeDir | Out-Null
    Copy-Item (Join-Path $sourceDir 'ILMerge.exe') $ilmergeDir -Force
    Copy-Item (Join-Path $sourceDir 'System.Compiler.dll') $ilmergeDir -Force
    Remove-Item $extractDir -Recurse -Force
    Write-Detail 'ILMerge ready'
    return $ilmergeExe
}

function Get-ProjectVersion {
    [xml]$projectXml = Get-Content -Raw $project
    $version = $projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) {
        throw "Could not read the <Version> property from $project."
    }
    return $version
}

function Get-IlMergePlatform([string]$Framework) {
    switch ($Framework) {
        'net20' { return 'v2' }
        'net40' { return 'v4' }
        'net472' { return 'v4' }
        default { throw "Unsupported target framework: $Framework" }
    }
}

try {
    New-Item -ItemType Directory -Force -Path $toolsDir, $downloadsDir, $distDir | Out-Null

    $dotnetExe = Get-DotNetSdk
    $ilmergeExe = Get-IlMerge

    # Keep the SDK, NuGet cache and CLI home inside the repository.
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_HOME = Join-Path $toolsDir 'home'
    $env:NUGET_PACKAGES = Join-Path $toolsDir 'nuget-packages'

    if ($Clean) {
        Write-Step 'Cleaning previous outputs'
        & $dotnetExe clean $project -c $Configuration "-p:TargetFrameworks=$TargetFramework" --nologo
        Get-ChildItem -Path $distDir -Filter 'ISCSIConsole*' | Remove-Item -Force
    }

    Write-Step "Building $Configuration | $TargetFramework"
    & $dotnetExe build $project -c $Configuration "-p:TargetFrameworks=$TargetFramework" --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "The build failed with exit code $LASTEXITCODE."
    }

    $binDir = Join-Path $root "ISCSIConsole\bin\$Configuration\$TargetFramework"
    $assemblies = @(
        (Join-Path $binDir 'ISCSIConsole.exe'),
        (Join-Path $binDir 'Utilities.dll'),
        (Join-Path $binDir 'DiskAccessLibrary.dll'),
        (Join-Path $binDir 'DiskAccessLibrary.Win32.dll'),
        (Join-Path $binDir 'ISCSI.dll')
    )
    foreach ($assembly in $assemblies) {
        if (-not (Test-Path $assembly)) {
            throw "Build output not found: $assembly"
        }
    }

    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path $frameworkDir)) {
        $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    }

    Write-Step 'Merging into a single executable'
    $mergedExe = Join-Path $distDir 'ISCSIConsole.exe'
    $platform = Get-IlMergePlatform $TargetFramework
    $mergeArgs = @(
        "/targetplatform:$platform,$frameworkDir",
        '/ndebug',
        '/target:winexe',
        "/out:$mergedExe"
    ) + $assemblies
    & $ilmergeExe @mergeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "ILMerge failed with exit code $LASTEXITCODE."
    }
    Write-Detail "$mergedExe"

    if (-not $NoPackage) {
        $version = Get-ProjectVersion
        $stageDir = Join-Path $distDir 'package'
        if (Test-Path $stageDir) {
            Remove-Item $stageDir -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

        $packageFiles = @('ISCSIConsole.exe')
        Copy-Item $mergedExe $stageDir
        $settingsTemplate = Join-Path $root 'packaging\ISCSIConsole.settings.ini'
        if (Test-Path $settingsTemplate) {
            Copy-Item $settingsTemplate $stageDir
            $packageFiles += 'ISCSIConsole.settings.ini'
        }

        $zipPath = Join-Path $distDir "ISCSIConsole_${version}_$TargetFramework.zip"
        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }
        $tar = Get-SystemTar
        if ($tar) {
            & $tar -a -c -f $zipPath -C $stageDir @packageFiles | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create $zipPath."
            }
        }
        else {
            Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath
        }
        Remove-Item $stageDir -Recurse -Force

        Write-Step 'Package ready'
        Write-Detail $zipPath
        Write-Detail ("SHA256: " + (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant())
    }

    Write-Host ''
    Write-Host 'Build completed.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
