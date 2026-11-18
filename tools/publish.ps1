# Builds every file of a release into artifacts/release-<version>, ready for upload. Usage:
#   tools/publish.ps1 [-Rids win-x64,linux-x64,linux-arm64] [-ReleaseDir artifacts/release-0.1.26] [-PublishDir artifacts/publish-0.1.26]
#                     [-PreviousReleaseDir artifacts/release-0.1.25] [-NoInstaller] [-NoAppImage]
# -ReleaseDir / -PublishDir (relative to the repository, or absolute) default to artifacts/release-<version> and artifacts/publish-<version>.
#
# Two editions (0.1.26), the same features and the same data folder:
# - Portable: the trimmed single-file program per system (tools/package.py): TMConcept-Portable-win-x64.exe,
#   tm-concept-portable-linux-x64.tar.gz, tm-concept-portable-linux-arm64.tar.gz. No version in the names; they are the only files that end
#   with -win-x64.exe / -linux-x64.tar.gz / -linux-arm64.tar.gz, which the updaters of 0.1.25 and older look for (checked below).
# - Installed (Velopack, vpk from .config/dotnet-tools.json): the trimmed program as a folder (not single-file: Velopack's delta packages
#   patch file by file, and a compressed single file changes almost entirely with every build), packed by `vpk pack` into
#   TMConcept-win-x64-Setup.exe, TMConcept-<version>-win-x64-full.nupkg, a delta package from the previous version when its full package is
#   found (-PreviousReleaseDir, otherwise the newest artifacts/release-<older version> or release-<older version> next to -ReleaseDir), and
#   releases.win-x64.json listing only this release's packages (Velopack's GitHub source downloads each package from the release that lists
#   it). Velopack's own portable zip, RELEASES and assets.*.json are not part of a release. -NoInstaller skips this edition.
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
# The taskbar id the app gives its windows (TaskbarIcon.AppId): the shortcuts Setup makes carry the same one, so a pinned button groups.
$aumid = 'TMAutoClip.TMConcept'
Write-Output "Building $version from commit $commit into $releaseFolder"
New-Item -ItemType Directory -Force -Path $releaseFolder | Out-Null

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

# The newest full package of this channel with a lower version: vpk makes the delta package from it.
function Find-PreviousFull([string]$channel) {
    $folders = @()
    if ($PreviousReleaseDir) {
        $folders += if ([IO.Path]::IsPathRooted($PreviousReleaseDir)) { $PreviousReleaseDir } else { Join-Path $root $PreviousReleaseDir }
    } else {
        $folders += Get-ChildItem -Directory -Path (Join-Path $root 'artifacts') -Filter 'release-*' -ErrorAction SilentlyContinue | ForEach-Object FullName
        $folders += Get-ChildItem -Directory -Path (Split-Path $releaseFolder -Parent) -Filter 'release-*' -ErrorAction SilentlyContinue | ForEach-Object FullName
    }
    $current = [version]$version
    $best = $null
    foreach ($folder in ($folders | Where-Object { $_ } | Select-Object -Unique)) {
        foreach ($file in Get-ChildItem -File -Path $folder -Filter "$packId-*-$channel-full.nupkg" -ErrorAction SilentlyContinue) {
            if ($file.Name -match "^$packId-(\d+\.\d+\.\d+)-$([regex]::Escape($channel))-full\.nupkg$") {
                $found = [version]$Matches[1]
                if ($found -lt $current -and ($null -eq $best -or $found -gt $best.Version)) { $best = [pscustomobject]@{ Version = $found; File = $file.FullName } }
            }
        }
    }
    return $best
}

# vpk pack into a fresh work folder; the release gets the installer / AppImage, this version's packages and an index of them only.
function Pack-Installed([string]$rid, [string]$folder, [string[]]$extra) {
    $channel = $rid
    $work = Join-Path $publishFolder "velopack-$rid"
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $previous = Find-PreviousFull $channel
    if ($previous) {
        Copy-Item $previous.File $work
        Write-Output "Delta package for $($rid): from $($previous.Version) ($($previous.File))"
    } else {
        Write-Output "No delta package for $($rid): no full package of an older version was found (the first installed release has none)."
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
$unused = @($files | Where-Object { $_.Name -like '*-Portable.zip' -or $_.Name -like 'RELEASES*' -or $_.Name -like 'assets.*.json' })
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
