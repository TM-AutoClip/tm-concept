#!/usr/bin/env sh
# Publishes self-contained single-file builds and packs them (artifacts/release). Usage: tools/publish.sh [rid ...]
# RELEASE_DIR / PUBLISH_DIR (environment, relative to the repository or absolute) keep an earlier release's files untouched while it is
# still being tried, e.g. RELEASE_DIR=artifacts/release-0.1.4 PUBLISH_DIR=artifacts/publish-0.1.4 tools/publish.sh
#
# A release is built only from a clean working tree (commit the version bump first): the binaries carry the commit they were built from
# (informational version "<version>+<commit>"), so that commit must hold exactly the code that was built. Tag that commit.
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
if [ -n "$(git -C "$root" status --porcelain)" ]; then
  echo "The working tree has uncommitted changes; commit them first (the binaries must match their commit):" >&2
  git -C "$root" status --porcelain >&2
  exit 1
fi
commit=$(git -C "$root" rev-parse HEAD)
release_dir=${RELEASE_DIR:-artifacts/release}
publish_dir=${PUBLISH_DIR:-artifacts/publish}
case "$release_dir" in /*) ;; *) release_dir="$root/$release_dir" ;; esac
case "$publish_dir" in /*) ;; *) publish_dir="$root/$publish_dir" ;; esac
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)
echo "Building $version from commit $commit into $release_dir"
[ "$#" -gt 0 ] || set -- win-x64 linux-x64 linux-arm64
for rid in "$@"; do
  out="$publish_dir/$rid"
  # Compressed inside the single file on every system (0.1.3, measured: since the trimmed build the compressed and the uncompressed file
  # use the same memory within 2-5 MB, and the compressed one is half the size on disk).
  compress=true
  dotnet publish "$root/src/TMConcept.Desktop/TMConcept.Desktop.csproj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile="$compress" -p:DebugType=none -p:DebugSymbols=false \
    -p:SourceRevisionId="$commit" -m:1 -nodeReuse:false -o "$out"
  python3 "$root/tools/package.py" "$out" "$rid" "$version" "$release_dir"
done
if [ -n "$(git -C "$root" status --porcelain)" ]; then
  echo "The build changed tracked files; the binaries may not match commit $commit." >&2
  exit 1
fi
