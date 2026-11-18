# Builds every file of a release into artifacts/release-<version>, ready for upload. Usage:
#   tools/publish.ps1 [-Rids win-x64,linux-x64,linux-arm64] [-ReleaseDir artifacts/release-0.1.26] [-PublishDir artifacts/publish-0.1.26]
#                     [-PreviousReleaseDir artifacts/release-0.1.25] [-NoDelta] [-NoInstaller] [-NoAppImage]
# -ReleaseDir / -PublishDir (relative to the repository, or absolute) default to artifacts/release-<version> and artifacts/publish-<version>.
#
# Two editions (0.1.26), the same features and the same data folder:
# - Portable: the trimmed single-file program per system (tools/package.py): TMConcept-Portable-win-x64.exe,
#   tm-concept-portable-linux-x64.tar.gz, tm-concept-portable-linux-arm64.tar.gz. No version in the names; they are the only files that end
#   with -win-x64.exe / -linux-x64.tar.gz / -linux-arm64.tar.gz, which the updaters of 0.1.25 and older look for (checked below).
# - Installed (Velopack, vpk from .config/dotnet-tools.json): the trimmed program as a folder (not single-file: Velopack's delta packages
#   patch file by file, and a compressed single file changes almost entirely with every build), packed by `vpk pack` into
#   TMConcept-win-x64-Setup.exe, TMConcept-<version>-win-x64-full.nupkg, a delta package from the previous PUBLISHED version, and
#   releases.win-x64.json listing only this release's packages (Velopack's GitHub source downloads each package from the release that lists
#   it). Velopack's own portable zip, RELEASES and assets.*.json are not part of a release. -NoInstaller skips this edition.
#   The delta is cut against the full package installed computers really have: the newest older release on GitHub that has a full package
#   of the channel, checked against that release's "## SHA-256" block (and GitHub's digest). A local copy (-PreviousReleaseDir, otherwise
#   artifacts/release-<that version> or release-<that version> next to -ReleaseDir) is used only when its SHA-256 is the published one;
#   otherwise the published package is downloaded. Rebuilds are not bit-identical, so a delta from a package that was never uploaded would
#   fail on every client. No reachable GitHub, or a published package that does not match its notes, stops the build; -NoDelta builds
#   without a delta package.
# - Linux AppImage (vpk [linux] pack, built on this Windows machine with vpk's own tools): TMConcept-linux-<arch>.AppImage, its full package
#   and releases.linux-<arch>.json. Not tried on Linux yet, so the README does not offer it; -NoAppImage skips it.
# The end prints the SHA-256 lines of every file for the release notes' "## SHA-256" block (the app checks its downloads against them).
#
# A release is built only from a clean working tree (commit the version bump first): the binaries carry the commit they were built from
# (informational version "<version>+<commit>"), so that commit must hold exactly the code that was built. Tag that commit.
# Never run the Setup or the AppImage this script makes on the build machine (it would install into your own profile).
param(
    [string[]]$Rids = @('win-x64', 'linux-x64', 'linux-arm64'),
    [string]$ReleaseDir = '',
    [string]$PublishDir = '',
    [string]$PreviousReleaseDir = '',
    [switch]$NoDelta,
    [switch]$NoInstaller,
    [switch]$NoAppImage
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dirty = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'git status failed; a release is built from a git working tree.' }
if ($dirty) {
    throw "The working tree has uncommitted changes; commit them first (the binaries must match their commit):`n$($dirty -join "`n")"
}
$commit = (& git -C $root rev-parse HEAD).Trim()
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $ReleaseDir) { $ReleaseDir = "artifacts/release-$version" }
if (-not $PublishDir) { $PublishDir = "artifacts/publish-$version" }
$releaseFolder = if ([IO.Path]::IsPathRooted($ReleaseDir)) { $ReleaseDir } else { Join-Path $root $ReleaseDir }
$publishFolder = if ([IO.Path]::IsPathRooted($PublishDir)) { $PublishDir } else { Join-Path $root $PublishDir }
$project = Join-Path $root 'src/TMConcept.Desktop/TMConcept.Desktop.csproj'
$packId = 'TMConcept'
# The GitHub repository the releases are published to (the delta package's base is read from there).
$repository = 'TM-AutoClip/tm-concept'
# The taskbar id the app gives its windows (TaskbarIcon.AppId): the shortcuts Setup makes carry the same one, so a pinned button groups.
$aumid = 'TMAutoClip.TMConcept'
Write-Output "Building $version from commit $commit into $releaseFolder"
New-Item -ItemType Directory -Force -Path $releaseFolder | Out-Null
New-Item -ItemType Directory -Force -Path $publishFolder | Out-Null

function Invoke-Checked([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

function Assert-ProductVersion([string]$exe) {
    # The exe's version resource holds the informational version: it must name the commit. Only Windows reads a PE version resource
    # (FileVersionInfo on Linux sees none), so a Windows build made on Linux is not checked here.
    if ([Environment]::OSVersion.Platform -ne 'Win32NT') { return }
    $product = (Get-Item $exe).VersionInfo.ProductVersion
    if ($product -ne "$version+$commit") { throw "$exe says '$product', expected '$version+$commit'." }
}

function Publish-Program([string]$rid, [bool]$singleFile, [string]$out) {
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    $single = if ($singleFile) { 'true' } else { 'false' }
    # Compressed inside the single file on every system (0.1.3, measured: since the trimmed build the compressed and the uncompressed
    # file use the same memory within 2-5 MB, and the compressed one is half the size on disk). The folder build has nothing to compress.
    Invoke-Checked "publish $rid (single file: $single)" {
        & dotnet publish $project -c Release -r $rid --self-contained true `
            -p:PublishSingleFile=$single -p:IncludeNativeLibrariesForSelfExtract=$single -p:EnableCompressionInSingleFile=$single `
            -p:DebugType=none -p:DebugSymbols=false -p:SourceRevisionId=$commit -m:1 -nodeReuse:false -o $out
    }
}

# The project's releases on GitHub (public API, no key), newest first; read once.
$script:publishedReleases = $null
function Get-PublishedReleases {
    if ($null -eq $script:publishedReleases) {
        try {
            $script:publishedReleases = @(Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases?per_page=50" `
                -Headers @{ 'User-Agent' = 'tm-concept-publish'; 'Accept' = 'application/vnd.github+json' })
        } catch {
            throw "The published releases could not be read from GitHub ($($_.Exception.Message)); the delta package must be cut from the published full package. Use -NoDelta to build without one."
        }
    }
    return $script:publishedReleases
}

# The full package of this channel that the newest older PUBLISHED release carries, checked against that release's SHA-256 block: a local
# copy when it is that file, otherwise the published one downloaded into $work. $null when that release has none (the first installed
# release, or one without this channel), so there is no delta.
function Find-PreviousFull([string]$channel, [string]$work) {
    if ($NoDelta) { return $null }
    $current = [version]$version
    $previous = Get-PublishedReleases | Where-Object { -not $_.draft -and -not $_.prerelease -and $_.tag_name -match '^v(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -lt $current } |
        Sort-Object { [version]($_.tag_name.Substring(1)) } -Descending | Select-Object -First 1
    if (-not $previous) { return $null }
    $previousVersion = $previous.tag_name.Substring(1)
    $name = "$packId-$previousVersion-$channel-full.nupkg"
    $asset = @($previous.assets | Where-Object { $_.name -eq $name }) | Select-Object -First 1
    if (-not $asset) {
        Write-Host "Release $($previous.tag_name) has no $($name): no delta package for $($channel)."
        return $null
    }

    $line = [regex]::Match([string]$previous.body, "(?m)^([0-9a-f]{64})\s+$([regex]::Escape($name))(\s|$)")
    if (-not $line.Success) { throw "Release $($previous.tag_name) carries $name, but its notes give no SHA-256 for it." }
    $published = $line.Groups[1].Value
    if ($asset.digest -and $asset.digest -ne "sha256:$published") {
        throw "Release $($previous.tag_name): GitHub's digest of $name ($($asset.digest)) is not the one in its notes ($published)."
    }

    $folders = @()
    if ($PreviousReleaseDir) {
        $folders += if ([IO.Path]::IsPathRooted($PreviousReleaseDir)) { $PreviousReleaseDir } else { Join-Path $root $PreviousReleaseDir }
    }
    $folders += Join-Path $root "artifacts/release-$previousVersion"
    $folders += Join-Path (Split-Path $releaseFolder -Parent) "release-$previousVersion"
    foreach ($folder in ($folders | Select-Object -Unique)) {
        $candidate = Join-Path $folder $name
        if (-not (Test-Path $candidate)) { continue }
        if ((Get-FileHash -Algorithm SHA256 $candidate).Hash.ToLowerInvariant() -eq $published) {
            return [pscustomobject]@{ Version = $previousVersion; File = $candidate }
        }
        Write-Warning "$candidate is not the published $name (a rebuild that was never uploaded?); the published one is downloaded."
    }

    $download = Join-Path $publishFolder "previous-$name"
    $progress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $download -Headers @{ 'User-Agent' = 'tm-concept-publish' }
    } catch {
        throw "The published $name could not be downloaded ($($_.Exception.Message)). Use -NoDelta to build without a delta package."
    } finally {
        $ProgressPreference = $progress
    }
    $hash = (Get-FileHash -Algorithm SHA256 $download).Hash.ToLowerInvariant()
    if ($hash -ne $published) {
        Remove-Item $download -Force
        throw "The downloaded $name has SHA-256 $hash, not the $published of the release notes."
    }
    return [pscustomobject]@{ Version = $previousVersion; File = $download }
}

# vpk pack into a fresh work folder; the release gets the installer / AppImage, this version's packages and an index of them only.
function Pack-Installed([string]$rid, [string]$folder, [string[]]$extra) {
    $channel = $rid
    $work = Join-Path $publishFolder "velopack-$rid"
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $previous = Find-PreviousFull $channel $work
    if ($previous) {
        # vpk finds the base by its file name in the output folder.
        Copy-Item $previous.File (Join-Path $work "$packId-$($previous.Version)-$channel-full.nupkg")
        Write-Output "Delta package for $($rid): from the published $($previous.Version) ($($previous.File))"
    } elseif ($NoDelta) {
        Write-Output "No delta package for $($rid) (-NoDelta)."
    } else {
        Write-Output "No delta package for $($rid): the previous published release has no full package of it."
    }

    $notes = Join-Path $root "docs/release-notes/v$version.md"
    # A cross-compile directive ([linux]) must be the first argument.
    $arguments = $extra + @('--skip-updates', 'pack', '--packId', $packId, '--packVersion', $version, '--packDir', $folder,
        '--runtime', $rid, '--channel', $channel, '--packTitle', 'TM Concept', '--packAuthors', 'TM AutoClip', '--outputDir', $work, '--delta', 'BestSpeed')
    if (Test-Path $notes) { $arguments += @('--releaseNotes', $notes) }
    if ($rid.StartsWith('win')) {
        $arguments += @('--mainExe', 'TMConcept.exe', '--icon', (Join-Path $root 'assets/brand/tm-concept.ico'), '--noPortable', '--aumid', $aumid)
    } else {
        $arguments += @('--mainExe', 'TMConcept', '--icon', (Join-Path $root 'assets/brand/tm-concept-icon-256.png'))
    }
    Push-Location $root
    try {
        Invoke-Checked "vpk pack $rid" { & dotnet vpk @arguments }
    } finally {
        Pop-Location
    }

    $index = Join-Path $work "releases.$channel.json"
    $feed = Get-Content -Raw -Encoding UTF8 $index | ConvertFrom-Json
    $mine = @($feed.Assets | Where-Object { $_.Version -eq $version })
    if ($mine.Count -eq 0) { throw "vpk listed no package of $version in $index" }
    foreach ($asset in $mine) { Copy-Item (Join-Path $work $asset.FileName) $releaseFolder -Force }
    # Only this release's packages: Velopack's GitHub source downloads each listed file from the release that lists it.
    $json = ConvertTo-Json -InputObject ([pscustomobject]@{ Assets = $mine }) -Depth 6
    [IO.File]::WriteAllText((Join-Path $releaseFolder "releases.$channel.json"), $json, (New-Object Text.UTF8Encoding $false))
    $installer = if ($rid.StartsWith('win')) { "$packId-$channel-Setup.exe" } else { "$packId-$channel.AppImage" }
    if (-not (Test-Path (Join-Path $work $installer))) { throw "vpk made no $installer" }
    Copy-Item (Join-Path $work $installer) $releaseFolder -Force
}

Invoke-Checked 'dotnet tool restore' { & dotnet tool restore --tool-manifest (Join-Path $root '.config/dotnet-tools.json') }
foreach ($rid in $Rids) {
    $out = Join-Path $publishFolder $rid
    Publish-Program $rid $true $out
    if ($rid.StartsWith('win')) { Assert-ProductVersion (Join-Path $out 'TMConcept.exe') }
    Invoke-Checked "packing $rid" { & python (Join-Path $PSScriptRoot 'package.py') $out $rid $version $releaseFolder }

    $windows = $rid.StartsWith('win')
    if (($windows -and -not $NoInstaller) -or (-not $windows -and -not $NoAppImage)) {
        $folder = Join-Path $publishFolder "$rid-installed"
        Publish-Program $rid $false $folder
        if ($windows) {
            Assert-ProductVersion (Join-Path $folder 'TMConcept.exe')
            Pack-Installed $rid $folder @()
        } else {
            Pack-Installed $rid $folder @('[linux]')
        }
    }
}

# Exactly one file per system ends the way the updaters of 0.1.25 and older look for, and it is the portable one.
$files = Get-ChildItem -File $releaseFolder
foreach ($rid in $Rids) {
    $suffix = if ($rid.StartsWith('win')) { "-$rid.exe" } else { "-$rid.tar.gz" }
    $portable = if ($rid.StartsWith('win')) { "TMConcept-Portable-$rid.exe" } else { "tm-concept-portable-$rid.tar.gz" }
    $matching = @($files | Where-Object { $_.Name.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase) })
    if ($matching.Count -ne 1 -or $matching[0].Name -ne $portable) {
        throw "Exactly one file must end with $suffix and it must be $portable; found: $(($matching | ForEach-Object Name) -join ', ')"
    }
}

# Every package an index names is in the release; nothing of Velopack's that the release does not use slipped in.
foreach ($index in $files | Where-Object { $_.Name -like 'releases.*.json' }) {
    foreach ($asset in (Get-Content -Raw -Encoding UTF8 $index.FullName | ConvertFrom-Json).Assets) {
        if (-not (Test-Path (Join-Path $releaseFolder $asset.FileName))) { throw "$($index.Name) names $($asset.FileName), which is not in the release" }
    }
}
$unused = @($files | Where-Object { $_.Name -like '*-Portable.zip' -or $_.Name -clike 'RELEASES*' -or $_.Name -like 'assets.*.json' })
if ($unused.Count -gt 0) { throw "Files the release does not use: $(($unused | ForEach-Object Name) -join ', ')" }

Write-Output ''
Write-Output '## SHA-256'
Write-Output ''
Write-Output '```'
foreach ($file in Get-ChildItem -File $releaseFolder | Sort-Object Name) {
    $hash = (Get-FileHash -Algorithm SHA256 $file.FullName).Hash.ToLowerInvariant()
    Write-Output ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}  {1}  ({2:N1} MiB)', $hash, $file.Name, ($file.Length / 1MB)))
}
Write-Output '```'

$after = & git -C $root status --porcelain
if ($after) { throw "The build changed tracked files; the binaries may not match commit $($commit):`n$($after -join "`n")" }
