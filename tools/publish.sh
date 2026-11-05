#!/usr/bin/env sh
# Publishes self-contained single-file builds and packs them (artifacts/release). Usage: tools/publish.sh [rid ...]
#
# A release is built only from a clean working tree (commit the version bump first): the binaries carry the commit they were built from
# (informational version "0.1.2+<commit>"), so that commit must hold exactly the code that was built. Tag that commit.
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
if [ -n "$(git -C "$root" status --porcelain)" ]; then
  echo "The working tree has uncommitted changes; commit them first (the binaries must match their commit):" >&2
  git -C "$root" status --porcelain >&2
  exit 1
fi
commit=$(git -C "$root" rev-parse HEAD)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)
echo "Building $version from commit $commit"
[ "$#" -gt 0 ] || set -- win-x64 linux-x64 linux-arm64
for rid in "$@"; do
  out="$root/artifacts/publish/$rid"
  dotnet publish "$root/src/TMConcept.Desktop/TMConcept.Desktop.csproj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false \
    -p:SourceRevisionId="$commit" -m:1 -nodeReuse:false -o "$out"
  python3 "$root/tools/package.py" "$out" "$rid" "$version"
done
if [ -n "$(git -C "$root" status --porcelain)" ]; then
  echo "The build changed tracked files; the binaries may not match commit $commit." >&2
  exit 1
fi
