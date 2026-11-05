#!/usr/bin/env sh
# Publishes self-contained single-file builds and packs them (artifacts/release). Usage: tools/publish.sh [rid ...]
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)
[ "$#" -gt 0 ] || set -- win-x64 linux-x64 linux-arm64
for rid in "$@"; do
  out="$root/artifacts/publish/$rid"
  dotnet publish "$root/src/TMConcept.Desktop/TMConcept.Desktop.csproj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false \
    -m:1 -nodeReuse:false -o "$out"
  python3 "$root/tools/package.py" "$out" "$rid" "$version"
done
