# Publishes self-contained single-file builds and packs them (artifacts/release). Usage: tools/publish.ps1 [-Rids win-x64,linux-x64,linux-arm64]
#
# A release is built only from a clean working tree (commit the version bump first): the binaries carry the commit they were built from
# (informational version "0.1.3+<commit>"), so that commit must hold exactly the code that was built. Tag that commit.
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
    # Compressed inside the single file on every system (0.1.3, measured: since the trimmed build the compressed and the uncompressed
    # file use the same memory within 2-5 MB, and the compressed one is half the size on disk).
    $compress = 'true'
    & dotnet publish (Join-Path $root 'src/TMConcept.Desktop/TMConcept.Desktop.csproj') -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$compress -p:DebugType=none -p:DebugSymbols=false `
        -p:SourceRevisionId=$commit -m:1 -nodeReuse:false -o $out
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }
    if ($rid.StartsWith('win') -and [Environment]::OSVersion.Platform -eq 'Win32NT') {
        # The exe's version resource holds the informational version: it must name the commit. Only Windows reads a PE version
        # resource (FileVersionInfo on Linux sees none), so a Windows build made on Linux is not checked here.
        $product = (Get-Item (Join-Path $out 'TMConcept.exe')).VersionInfo.ProductVersion
        if ($product -ne "$version+$commit") { throw "The exe says '$product', expected '$version+$commit'." }
    }
    & python (Join-Path $PSScriptRoot 'package.py') $out $rid $version
    if ($LASTEXITCODE -ne 0) { throw "packing failed for $rid" }
}
$after = & git -C $root status --porcelain
if ($after) { throw "The build changed tracked files; the binaries may not match commit $($commit):`n$($after -join "`n")" }
