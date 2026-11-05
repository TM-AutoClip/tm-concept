# Publishes self-contained single-file builds and packs them (artifacts/release). Usage: tools/publish.ps1 [-Rids win-x64,linux-x64,linux-arm64]
#
# A release is built only from a clean working tree (commit the version bump first): the binaries carry the commit they were built from
# (informational version "0.1.2+<commit>"), so that commit must hold exactly the code that was built. Tag that commit.
param([string[]]$Rids = @('win-x64', 'linux-x64', 'linux-arm64'))
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
Write-Output "Building $version from commit $commit"
foreach ($rid in $Rids) {
    $out = Join-Path $root "artifacts/publish/$rid"
    # Linux: no compression inside the single file. A compressed bundle is unpacked into private memory at start (about 35 MB more
    # on Windows measurements; the Pi showed ~230 MB with the window open), while an uncompressed one is mapped from the file. The tar.gz
    # compresses it for the download anyway. Windows keeps it compressed: the exe is downloaded as is (59 MB instead of 129 MB).
    $compress = if ($rid.StartsWith('win')) { 'true' } else { 'false' }
    & dotnet publish (Join-Path $root 'src/TMConcept.Desktop/TMConcept.Desktop.csproj') -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$compress -p:DebugType=none -p:DebugSymbols=false `
        -p:SourceRevisionId=$commit -m:1 -nodeReuse:false -o $out
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }
    if ($rid.StartsWith('win')) {
        # The exe's version resource holds the informational version: it must name the commit.
        $product = (Get-Item (Join-Path $out 'TMConcept.exe')).VersionInfo.ProductVersion
        if ($product -ne "$version+$commit") { throw "The exe says '$product', expected '$version+$commit'." }
    }
    & python (Join-Path $PSScriptRoot 'package.py') $out $rid $version
    if ($LASTEXITCODE -ne 0) { throw "packing failed for $rid" }
}
$after = & git -C $root status --porcelain
if ($after) { throw "The build changed tracked files; the binaries may not match commit $($commit):`n$($after -join "`n")" }
