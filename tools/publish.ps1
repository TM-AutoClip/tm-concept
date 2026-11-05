# Publishes self-contained single-file builds and packs them (artifacts/release). Usage: tools/publish.ps1 [-Rids win-x64,linux-x64,linux-arm64]
param([string[]]$Rids = @('win-x64', 'linux-x64', 'linux-arm64'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
foreach ($rid in $Rids) {
    $out = Join-Path $root "artifacts/publish/$rid"
    & dotnet publish (Join-Path $root 'src/TMConcept.Desktop/TMConcept.Desktop.csproj') -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false `
        -m:1 -nodeReuse:false -o $out
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }
    & python (Join-Path $PSScriptRoot 'package.py') $out $rid $version
    if ($LASTEXITCODE -ne 0) { throw "packing failed for $rid" }
}
