[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    # Root of the repository to build. Defaults to the folder this script lives in.
    [string]$RepoRoot,

    # The packaged app project, as a .csproj path or a project name. Only needed when
    # the repo has more than one project with a Package.appxmanifest next to it.
    [string]$Project,

    # Release notes for the GitHub release. When omitted, GitHub generates notes
    # from the commits/PRs since the previous release.
    [string]$Notes,

    # Mark the GitHub release as a pre-release.
    [switch]$Prerelease,

    # Build and sign only; skip tagging and the GitHub release.
    [switch]$SkipRelease,

    # Allow releasing with uncommitted changes (they would be in the build but not in the tag).
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# A packaged app project is a .csproj with a Package.appxmanifest next to it
# (test and library projects have none). Returns the matching .csproj files under $Root.
function Find-PackagedProject {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    Get-ChildItem -LiteralPath $Root -Filter '*.csproj' -File -Recurse -Depth 3 -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|AppPackages)\\' } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.DirectoryName 'Package.appxmanifest') } |
        Where-Object { -not $Project -or $_.BaseName -eq $Project }
}

# An explicit .csproj path pins both the project and (via git) the repo root.
$projectFile = $null
if ($Project -and $Project.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
    if (-not (Test-Path -LiteralPath $Project)) {
        throw "Project not found: $Project"
    }
    $projectFile = Get-Item -LiteralPath $Project
    if (-not $RepoRoot) {
        $RepoRoot = & git -C $projectFile.DirectoryName rev-parse --show-toplevel 2>$null
        if (-not $RepoRoot) {
            $RepoRoot = $projectFile.DirectoryName
        }
    }
}

# $PSScriptRoot is not reliable in every host (e.g. ISE/VS Code run-selection or
# dot-sourcing report the caller's folder), so resolve the root explicitly: an
# explicit -RepoRoot wins, then the script's own folder, then the git repo of the
# current directory. Only accept a candidate that actually contains a packaged project.
if (-not $RepoRoot) {
    $candidates = @(
        $(if ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path }),
        $PSScriptRoot,
        $(& git rev-parse --show-toplevel 2>$null),
        (Get-Location).ProviderPath
    ) | Where-Object { $_ }

    $RepoRoot = $candidates |
        Where-Object { Find-PackagedProject -Root $_ } |
        Select-Object -First 1

    if (-not $RepoRoot) {
        throw "Could not locate a repository with a packaged app project (tried: $($candidates -join ', ')). Pass -RepoRoot or run from inside the repo."
    }
}

if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "Repository root not found: $RepoRoot"
}

$repoRoot = (Resolve-Path -LiteralPath $RepoRoot).ProviderPath

if (-not $projectFile) {
    $found = @(Find-PackagedProject -Root $repoRoot)
    if ($found.Count -eq 0) {
        $what = if ($Project) { "named '$Project'" } else { 'with a Package.appxmanifest next to it' }
        throw "No project $what was found under $repoRoot."
    }
    if ($found.Count -gt 1) {
        throw "Found more than one packaged project; pass -Project with one of: $(($found | ForEach-Object BaseName) -join ', ')."
    }
    $projectFile = $found[0]
}

$appName = $projectFile.BaseName
$projectDir = $projectFile.DirectoryName
$projectPath = $projectFile.FullName
$manifestPath = Join-Path $projectDir 'Package.appxmanifest'
$outputRoot = Join-Path $projectDir 'AppPackages\GitHub'
$publishProfilesRoot = Join-Path $projectDir 'Properties\PublishProfiles'

# Signing is done by the maintainer's local 'sign-msix' profile function, so the
# signing configuration stays out of the repo.
$signCommand = 'sign-msix'

$builds = @(
    @{
        Name = 'x64'
        Platform = 'x64'
        RuntimeIdentifier = 'win-x64'
        PublishProfile = 'win-x64.pubxml'
        OutputDir = (Join-Path $outputRoot 'x64')
    },
    @{
        Name = 'arm64'
        Platform = 'ARM64'
        RuntimeIdentifier = 'win-arm64'
        PublishProfile = 'win-arm64.pubxml'
        OutputDir = (Join-Path $outputRoot 'arm64')
    }
)

function Get-ReleaseTag {
    param(
        [Parameter(Mandatory)]
        [string]$Version
    )

    # 0.1.0.0 -> v0.1.0 (matches existing tags); keep a non-zero fourth part.
    $parts = $Version.Split('.')
    if ($parts.Count -eq 4 -and $parts[3] -eq '0') {
        $parts = $parts[0..2]
    }
    return 'v' + ($parts -join '.')
}

function Get-GitHubRepository {
    $url = & git -C $repoRoot remote get-url origin 2>$null
    if ($LASTEXITCODE -ne 0 -or "$url" -notmatch 'github\.com[:/](?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(\.git)?/?$') {
        throw "Could not work out the GitHub owner/repository from the 'origin' remote ($url)."
    }
    return $Matches['repo']
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $entry = $Archive.GetEntry($Name)
    if (-not $entry) {
        throw "Missing package entry: $Name."
    }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Get-FrameworkPackages {
    $reference = Select-Xml -Path $projectPath -XPath "//PackageReference[@Include='Microsoft.WindowsAppSDK.Runtime']" |
        Select-Object -First 1
    if (-not $reference) {
        throw "Expected a pinned Microsoft.WindowsAppSDK.Runtime reference in $projectPath."
    }
    $runtimeVersion = $reference.Node.Version
    if (-not $runtimeVersion -or $runtimeVersion -notmatch '^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$') {
        throw "Pin Microsoft.WindowsAppSDK.Runtime to an exact version in $projectPath (found '$runtimeVersion')."
    }

    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget\packages' }
    $runtimeRoot = Join-Path $nugetRoot "microsoft.windowsappsdk.runtime\$runtimeVersion"
    if (-not (Test-Path -LiteralPath $runtimeRoot)) {
        & dotnet restore $projectPath | Out-Host
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $runtimeRoot)) {
            throw "Microsoft.WindowsAppSDK.Runtime $runtimeVersion is not in the NuGet cache ($runtimeRoot)."
        }
    }

    foreach ($build in $builds) {
        $sources = @(Get-ChildItem -LiteralPath (Join-Path $runtimeRoot "tools\MSIX\win10-$($build.Name)") -Filter 'Microsoft.WindowsAppRuntime.*.msix' -File |
            Where-Object { $_.Name -match '^Microsoft\.WindowsAppRuntime\.\d+\.msix$' })
        if ($sources.Count -ne 1) {
            throw "Expected one framework package for $($build.Name) under $runtimeRoot, found $($sources.Count)."
        }

        $zip = [System.IO.Compression.ZipFile]::OpenRead($sources[0].FullName)
        try {
            [xml]$frameworkManifest = Read-ZipEntryText -Archive $zip -Name 'AppxManifest.xml'
        }
        finally {
            $zip.Dispose()
        }
        $identity = $frameworkManifest.Package.Identity
        if ($identity.ProcessorArchitecture -cne $build.Name) {
            throw "Framework architecture does not match $($build.Name)."
        }
        [pscustomobject]@{
            Name = $identity.Name
            Publisher = $identity.Publisher
            Version = $identity.Version
            Architecture = $identity.ProcessorArchitecture
            Source = $sources[0].FullName
            AssetName = "$($identity.Name)_$($identity.Version)_$($identity.ProcessorArchitecture).msix"
        }
    }
}

function New-AppInstallerFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [string]$BundleName,
        [object[]]$FrameworkPackages = @()
    )

    $namespace = 'http://schemas.microsoft.com/appx/appinstaller/2021'
    $baseUri = "https://github.com/$Repository/releases"
    $assetBase = "$baseUri/download/$([uri]::EscapeDataString($tag))"

    $document = New-Object System.Xml.XmlDocument
    [void]$document.AppendChild($document.CreateXmlDeclaration('1.0', 'utf-8', $null))
    $installer = $document.CreateElement('AppInstaller', $namespace)
    $installer.SetAttribute('Version', $version)
    $installer.SetAttribute('Uri', "$baseUri/latest/download/$feedName")
    [void]$document.AppendChild($installer)

    $bundle = $document.CreateElement('MainBundle', $namespace)
    $bundle.SetAttribute('Name', $identityName)
    $bundle.SetAttribute('Publisher', $identityPublisher)
    $bundle.SetAttribute('Version', $version)
    $bundle.SetAttribute('Uri', "$assetBase/$([uri]::EscapeDataString($BundleName))")
    [void]$installer.AppendChild($bundle)

    if ($FrameworkPackages.Count -gt 0) {
        $dependencies = $document.CreateElement('Dependencies', $namespace)
        foreach ($framework in $FrameworkPackages) {
            $package = $document.CreateElement('Package', $namespace)
            $package.SetAttribute('Name', $framework.Name)
            $package.SetAttribute('Publisher', $framework.Publisher)
            $package.SetAttribute('Version', $framework.Version)
            $package.SetAttribute('ProcessorArchitecture', $framework.Architecture)
            $package.SetAttribute('Uri', "$assetBase/$([uri]::EscapeDataString($framework.AssetName))")
            [void]$dependencies.AppendChild($package)
        }
        [void]$installer.AppendChild($dependencies)
    }

    $settings = $document.CreateElement('UpdateSettings', $namespace)
    $onLaunch = $document.CreateElement('OnLaunch', $namespace)
    $onLaunch.SetAttribute('HoursBetweenUpdateChecks', '0')
    $onLaunch.SetAttribute('ShowPrompt', 'false')
    $onLaunch.SetAttribute('UpdateBlocksActivation', 'false')
    [void]$settings.AppendChild($onLaunch)
    [void]$settings.AppendChild($document.CreateElement('AutomaticBackgroundTask', $namespace))
    [void]$installer.AppendChild($settings)
    $document.Save($Path)
}

function Add-AutoUpdateReference {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $namespace = 'http://schemas.microsoft.com/appx/manifest/uap/windows10/13'
    $document = New-Object System.Xml.XmlDocument
    $document.PreserveWhitespace = $true
    $document.Load($Path)
    $root = $document.DocumentElement
    if ($root.Properties.GetElementsByTagName('AutoUpdate', $namespace).Count -gt 0) {
        throw "The source manifest already contains an AutoUpdate reference: $Path."
    }

    $root.SetAttribute('xmlns:uap13', $namespace)
    $ignorable = @($root.GetAttribute('IgnorableNamespaces') -split '\s+' | Where-Object { $_ })
    if ('uap13' -notin $ignorable) {
        $ignorable += 'uap13'
    }
    $root.SetAttribute('IgnorableNamespaces', ($ignorable -join ' '))
    $autoUpdate = $document.CreateElement('uap13', 'AutoUpdate', $namespace)
    $appInstaller = $document.CreateElement('uap13', 'AppInstaller', $namespace)
    $appInstaller.SetAttribute('File', 'Update.appinstaller')
    [void]$autoUpdate.AppendChild($appInstaller)
    [void]$root.Properties.AppendChild($autoUpdate)
    $document.Save($Path)
}

function Find-MakeAppx {
    $found = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
        Select-Object -First 1
    if (-not $found) {
        throw 'MakeAppx (Windows SDK) was not found.'
    }
    return $found.FullName
}

function Test-ReleaseBundle {
    param(
        [Parameter(Mandatory)] [string]$BundlePath,
        [Parameter(Mandatory)] [string]$FeedPath,
        [object[]]$FrameworkPackages = @()
    )

    [xml]$feed = Get-Content -LiteralPath $FeedPath -Raw
    $main = $feed.AppInstaller.MainBundle
    if ($feed.DocumentElement.NamespaceURI -cne 'http://schemas.microsoft.com/appx/appinstaller/2021' -or
        -not $main -or $feed.AppInstaller.MainPackage) {
        throw 'The feed must describe a MainBundle using the App Installer 2021 schema.'
    }
    if ($main.Version -cne $feed.AppInstaller.Version) {
        throw 'The feed and bundle versions must match.'
    }
    $releaseBase = "https://github.com/$repository/releases"
    $assetBase = "$releaseBase/download/$([uri]::EscapeDataString($tag))"
    if ($feed.AppInstaller.Uri -cne "$releaseBase/latest/download/$feedName" -or
        $main.Uri -cne "$assetBase/$([uri]::EscapeDataString([System.IO.Path]::GetFileName($BundlePath)))") {
        throw 'The feed must point to the stable update feed and the versioned bundle asset.'
    }
    $settings = $feed.AppInstaller.UpdateSettings
    if (-not $settings -or $settings.OnLaunch.HoursBetweenUpdateChecks -cne '0' -or
        $settings.OnLaunch.ShowPrompt -cne 'false' -or $settings.OnLaunch.UpdateBlocksActivation -cne 'false' -or
        -not $settings.SelectSingleNode("*[local-name()='AutomaticBackgroundTask']")) {
        throw 'The feed must enable silent, non-blocking launch checks and background updates.'
    }
    $feedHash = (Get-FileHash -LiteralPath $FeedPath -Algorithm SHA256).Hash

    $zip = [System.IO.Compression.ZipFile]::OpenRead($BundlePath)
    try {
        [xml]$bundleManifest = Read-ZipEntryText -Archive $zip -Name 'AppxMetadata/AppxBundleManifest.xml'
        foreach ($attribute in 'Name', 'Publisher', 'Version') {
            if ($bundleManifest.Bundle.Identity.GetAttribute($attribute) -cne $main.GetAttribute($attribute)) {
                throw "Bundle identity does not match the feed ($attribute)."
            }
        }

        $architectures = @()
        $applicationPackages = @($bundleManifest.Bundle.Packages.Package | Where-Object Type -eq 'application')
        foreach ($entry in $applicationPackages) {
            $architecture = $entry.Architecture
            if ($architecture -notin 'x64', 'arm64' -or $architecture -in $architectures) {
                throw "Unexpected or duplicate application architecture: $architecture."
            }
            $architectures += $architecture
            $inner = $zip.GetEntry($entry.FileName)
            if (-not $inner) {
                throw "Missing bundled package: $($entry.FileName)."
            }
            $stream = New-Object System.IO.MemoryStream
            try {
                $source = $inner.Open()
                try { $source.CopyTo($stream) } finally { $source.Dispose() }
                $stream.Position = 0
                $package = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
                try {
                    [xml]$manifest = Read-ZipEntryText -Archive $package -Name 'AppxManifest.xml'
                    foreach ($attribute in 'Name', 'Publisher', 'Version') {
                        if ($manifest.Package.Identity.GetAttribute($attribute) -cne $main.GetAttribute($attribute)) {
                            throw "$architecture package identity does not match the feed ($attribute)."
                        }
                    }
                    if ($manifest.Package.Identity.ProcessorArchitecture -cne $architecture) {
                        throw "$architecture package declares architecture $($manifest.Package.Identity.ProcessorArchitecture)."
                    }
                    $embeddedFeed = $package.GetEntry('Update.appinstaller')
                    if (-not $embeddedFeed) {
                        throw "The $architecture package has no embedded Update.appinstaller."
                    }
                    $source = $embeddedFeed.Open()
                    $hasher = [System.Security.Cryptography.SHA256]::Create()
                    try {
                        $embeddedHash = [System.BitConverter]::ToString($hasher.ComputeHash($source)).Replace('-', '')
                        if ($embeddedHash -cne $feedHash) {
                            throw "The Update.appinstaller embedded in the $architecture package differs from the published feed."
                        }
                    }
                    finally {
                        $hasher.Dispose()
                        $source.Dispose()
                    }
                    $namespaces = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
                    $namespaces.AddNamespace('p', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
                    $namespaces.AddNamespace('uap13', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/13')
                    $reference = $manifest.SelectSingleNode('/p:Package/p:Properties/uap13:AutoUpdate/uap13:AppInstaller', $namespaces)
                    if (-not $reference -or $reference.GetAttribute('File') -cne 'Update.appinstaller') {
                        throw "The $architecture manifest must reference the embedded Update.appinstaller."
                    }
                    if (-not $package.GetEntry('AppxBlockMap.xml')) {
                        throw "The $architecture package has no block map."
                    }
                    foreach ($dependency in $manifest.Package.Dependencies.PackageDependency) {
                        $framework = @($FrameworkPackages | Where-Object { $_.Name -ceq $dependency.Name -and $_.Architecture -ceq $architecture })
                        if ($framework.Count -ne 1 -or $framework[0].Publisher -cne $dependency.Publisher -or
                            [version]$framework[0].Version -lt [version]$dependency.MinVersion) {
                            throw "No matching release dependency for $($dependency.Name) on $architecture."
                        }
                    }
                }
                finally { $package.Dispose() }
            }
            finally { $stream.Dispose() }
        }
        foreach ($required in 'x64', 'arm64') {
            if ($required -notin $architectures) {
                throw "The bundle is missing the $required application package."
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    $dependencyNodes = @($feed.AppInstaller.Dependencies.Package | Where-Object { $_ })
    if ($dependencyNodes.Count -ne $FrameworkPackages.Count) {
        throw 'The feed dependency count does not match the release framework packages.'
    }
    foreach ($framework in $FrameworkPackages) {
        $nodes = @($dependencyNodes | Where-Object { $_.Name -ceq $framework.Name -and $_.ProcessorArchitecture -ceq $framework.Architecture })
        if ($nodes.Count -ne 1 -or $nodes[0].Version -cne $framework.Version -or $nodes[0].Publisher -cne $framework.Publisher) {
            throw "The feed does not list $($framework.Name) $($framework.Version) for $($framework.Architecture)."
        }
        if ($nodes[0].Uri -cne "$assetBase/$([uri]::EscapeDataString($framework.AssetName))" -or
            -not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $FeedPath) $framework.AssetName) -PathType Leaf)) {
            throw "The feed URI for $($framework.Name) $($framework.Architecture) does not match a release asset."
        }
    }
    Write-Host "  Verified bundle ($($architectures -join ', ')), identical embedded feeds, block maps and dependencies."
}

function Assert-ReleaseReady {
    param(
        [Parameter(Mandatory)]
        [string]$Tag
    )

    foreach ($tool in 'git', 'gh') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool is required but was not found on PATH."
        }
    }

    & gh auth status *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'gh is not authenticated. Run "gh auth login" first.'
    }

    $branch = (& git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -eq 'HEAD') {
        throw 'Could not determine the current branch (detached HEAD?).'
    }

    if (-not $AllowDirty) {
        $status = & git -C $repoRoot status --porcelain
        if ($status) {
            throw "Working tree has uncommitted changes; commit/stash them or pass -AllowDirty.`n$($status -join "`n")"
        }
    }

    & git -C $repoRoot fetch origin --quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'git fetch origin failed.'
    }

    $upstream = & git -C $repoRoot rev-parse --abbrev-ref '@{u}' 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Branch '$branch' has no upstream. Push it first (git push -u origin $branch)."
    }
    $ahead = [int](& git -C $repoRoot rev-list --count '@{u}..HEAD')
    if ($ahead -gt 0) {
        throw "Branch '$branch' is $ahead commit(s) ahead of $upstream. Push before releasing."
    }

    & git -C $repoRoot rev-parse -q --verify "refs/tags/$Tag" *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag $Tag already exists locally. Bump the version in Package.appxmanifest."
    }
    $remoteTag = & git -C $repoRoot ls-remote --tags origin "refs/tags/$Tag"
    if ($remoteTag) {
        throw "Tag $Tag already exists on origin. Bump the version in Package.appxmanifest."
    }

    # Drafts have no tag on origin yet, so also check for an existing (draft) release.
    Push-Location -LiteralPath $repoRoot
    try {
        # Filter in PowerShell rather than --jq: Windows PowerShell 5.1 strips embedded quotes from native args.
        $releasesJson = & gh release list --limit 100 --json tagName
        if ($LASTEXITCODE -ne 0) {
            throw 'gh release list failed.'
        }
    }
    finally {
        Pop-Location
    }
    $existingRelease = ($releasesJson | Out-String | ConvertFrom-Json) | Where-Object { $_.tagName -eq $Tag }
    if ($existingRelease) {
        throw "A release for $Tag already exists on GitHub (possibly a draft). Delete it or bump the version."
    }

    return $branch
}

function Invoke-GitHubMsixBuild {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Build
    )

    $appxPackageDir = (Join-Path $Build.OutputDir '')

    if (Test-Path -LiteralPath $Build.OutputDir) {
        Remove-Item -LiteralPath $Build.OutputDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Build.OutputDir -Force | Out-Null

    $arguments = @(
        'publish'
        $projectPath
        '-c'
        $Configuration
        "-p:Platform=$($Build.Platform)"
        "-p:RuntimeIdentifier=$($Build.RuntimeIdentifier)"
        "-p:PublishProfile=$(Join-Path $publishProfilesRoot $Build.PublishProfile)"
        '-p:GenerateAppxPackageOnBuild=true'
        '-p:UapAppxPackageBuildMode=SideloadOnly'
        '-p:AppxBundle=Never'
        '-p:AppxPackageSigningEnabled=false'
        '-p:PackageCertificateThumbprint='
        "-p:AppxPackageDir=$appxPackageDir"
        "-p:ReleaseAppInstallerPath=$script:embeddedFeedPath"
    )

    Write-Host "Building GitHub MSIX for $($Build.Name)..."
    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($Build.Name)."
    }
}

function Invoke-MsixSigning {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo[]]$Files
    )

    & $signCommand -Path $Files.FullName

    # Don't rely on the signing command's error reporting: confirm every package is signed.
    foreach ($file in $Files) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        if ($signature.Status -ne 'Valid') {
            throw "$($file.Name) is not validly signed (status: $($signature.Status))."
        }
        Write-Host "  Verified signature on $($file.Name): $($signature.SignerCertificate.Subject)"
    }
}

$scriptFailed = $false
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The dotnet SDK is required but was not found on PATH.'
    }

    foreach ($requiredFile in $projectPath, $manifestPath) {
        if (-not (Test-Path -LiteralPath $requiredFile)) {
            throw "Required file not found: $requiredFile"
        }
    }

    # The publish profiles are git-ignored (*.pubxml), so a fresh clone won't have them.
    foreach ($build in $builds) {
        $profilePath = Join-Path $publishProfilesRoot $build.PublishProfile
        if (-not (Test-Path -LiteralPath $profilePath)) {
            throw "Publish profile not found: $profilePath"
        }
    }

    if (-not (Get-Command $signCommand -ErrorAction SilentlyContinue)) {
        throw "'$signCommand' was not found. It is defined in the maintainer's PowerShell profile; run this script from a pwsh session that loaded it."
    }

    $manifestContent = [System.IO.File]::ReadAllText($manifestPath)
    $versionMatch = [regex]::Match($manifestContent, '(?s)<Identity\b[^>]*?\bVersion="([^"]+)"')
    if (-not $versionMatch.Success) {
        throw "Could not find Version attribute on <Identity> in $manifestPath."
    }
    $version = $versionMatch.Groups[1].Value
    $tag = Get-ReleaseTag -Version $version
    $versionedOutputDir = Join-Path $outputRoot "$version-gh"
    $identity = ([xml]$manifestContent).Package.Identity
    $identityName = $identity.Name
    $identityPublisher = $identity.Publisher
    $feedName = "$appName.appinstaller"
    $bundleName = "${appName}_$version.msixbundle"
    $repository = Get-GitHubRepository
    $makeAppx = Find-MakeAppx

    Write-Host "Project: $appName ($projectPath), version $version"

    # Check git/gh state before the (slow) build so problems surface immediately.
    if (-not $SkipRelease) {
        $branch = Assert-ReleaseReady -Tag $tag
        Write-Host "Releasing $tag from branch '$branch'."
    }

    if (Test-Path -LiteralPath $versionedOutputDir) {
        Get-ChildItem -LiteralPath $versionedOutputDir -File |
            Where-Object { $_.Extension -in '.msix', '.msixbundle', '.appinstaller' } |
            Remove-Item -Force
    }
    else {
        New-Item -ItemType Directory -Path $versionedOutputDir -Force | Out-Null
    }

    # Write once before either build so both packages embed the exact published feed.
    $frameworkPackages = @(Get-FrameworkPackages)
    $feedPath = Join-Path $versionedOutputDir $feedName
    $script:embeddedFeedPath = $feedPath
    New-AppInstallerFile -Path $feedPath -Repository $repository -BundleName $bundleName -FrameworkPackages $frameworkPackages

    $originalManifest = [System.IO.File]::ReadAllBytes($manifestPath)
    try {
        Add-AutoUpdateReference -Path $manifestPath
        foreach ($build in $builds) {
            Invoke-GitHubMsixBuild -Build $build
        }
    }
    finally {
        [System.IO.File]::WriteAllBytes($manifestPath, $originalManifest)
    }

    Write-Host 'Collecting MSIX packages...'
    $stagingDir = Join-Path $outputRoot 'bundle-staging'
    if (Test-Path -LiteralPath $stagingDir) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
    foreach ($build in $builds) {
        # Exclude Dependencies\* (e.g. Microsoft.WindowsAppRuntime.2.msix) - only collect the app's own package.
        $msixFiles = @(Get-ChildItem -LiteralPath $build.OutputDir -Recurse -Filter '*.msix' -File |
            Where-Object { $_.FullName -notmatch '\\Dependencies\\' })
        if (-not $msixFiles) {
            throw "No .msix file was found under $($build.OutputDir) for $($build.Name)."
        }
        if ($msixFiles.Count -gt 1) {
            throw "Expected one app package under $($build.OutputDir) for $($build.Name), found: $(($msixFiles | ForEach-Object Name) -join ', ')."
        }
        foreach ($msix in $msixFiles) {
            $destination = Join-Path $stagingDir $msix.Name
            Copy-Item -LiteralPath $msix.FullName -Destination $destination -Force
            Write-Host "  Staged $($msix.Name)"
        }
    }

    $bundlePath = Join-Path $versionedOutputDir $bundleName
    & $makeAppx bundle /d $stagingDir /p $bundlePath /bv $version /o | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw 'makeappx bundle failed.'
    }

    # Microsoft's framework packages keep their original signatures; sign only our bundle.
    Invoke-MsixSigning -Files @(Get-Item -LiteralPath $bundlePath)
    $assets = @(Get-Item -LiteralPath $bundlePath, $feedPath)
    foreach ($framework in $frameworkPackages) {
        $assets += Copy-Item -LiteralPath $framework.Source -Destination (Join-Path $versionedOutputDir $framework.AssetName) -Force -PassThru
    }
    Test-ReleaseBundle -BundlePath $bundlePath -FeedPath $feedPath -FrameworkPackages $frameworkPackages
    Write-Host "Release files are under $versionedOutputDir"
    Write-Host "Stable install/update URL: https://github.com/$repository/releases/latest/download/$feedName"
    if ($Prerelease) {
        Write-Warning 'Pre-releases are not served by the stable update URL.'
    }

    if (-not $SkipRelease) {
        $commit = (& git -C $repoRoot rev-parse HEAD).Trim()

        # Local-only tag; GitHub creates the remote tag at $commit when the draft is published.
        Write-Host "Creating local tag $tag at HEAD of '$branch' ($commit)..."
        & git -C $repoRoot tag $tag $commit
        if ($LASTEXITCODE -ne 0) {
            throw "git tag $tag failed."
        }

        # Always a draft: review and publish it manually on GitHub.
        $ghArguments = @('release', 'create', $tag) + $assets.FullName + @('--title', $tag, '--target', $commit, '--draft')
        $notesFile = $null
        if ($Notes) {
            # Pass notes via a file so quotes/newlines survive Windows PowerShell 5.1 native arg passing.
            $notesFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllText($notesFile, $Notes, [System.Text.UTF8Encoding]::new($false))
            $ghArguments += @('--notes-file', $notesFile)
        }
        else {
            $ghArguments += '--generate-notes'
        }
        if ($Prerelease) {
            $ghArguments += '--prerelease'
        }
        Write-Host "Creating draft GitHub release $tag..."
        Push-Location -LiteralPath $repoRoot
        try {
            & gh @ghArguments
            if ($LASTEXITCODE -ne 0) {
                throw "gh release create failed. Remove the local tag before retrying: git tag -d $tag"
            }
        }
        finally {
            Pop-Location
            if ($notesFile) {
                Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue
            }
        }

        Write-Host "Done. Draft release $tag is saved; publishing it on GitHub will create the tag at $commit."
    }
    else {
        Write-Host 'Done.'
    }
}
catch {
    $scriptFailed = $true
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
}

if ($scriptFailed -and $Host.Name -eq 'ConsoleHost') {
    Write-Host ''
    Read-Host 'Build failed. Press Enter to close this window'
}

if ($scriptFailed) {
    exit 1
}
