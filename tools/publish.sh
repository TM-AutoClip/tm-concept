#!/usr/bin/env sh
# Publishes the portable edition: self-contained single-file builds, packed by tools/package.py (artifacts/release-<version>).
# Usage: tools/publish.sh [--no-compression] [--ready-to-run] [rid ...]
# --no-compression and --ready-to-run are for start-up measurements only (0.1.40): an uncompressed single file
# (EnableCompressionInSingleFile=false) and precompiled trimmed assemblies (PublishReadyToRun=true). Without them the build is the one every
# release has used (compressed, no ReadyToRun); a release is built without them.
# RELEASE_DIR / PUBLISH_DIR (environment, relative to the repository or absolute) default to artifacts/release-<version> and
# artifacts/publish-<version>. The installed edition (Velopack Setup, packages, AppImage) and the checks of a whole release are made by
# tools/publish.ps1 on Windows; a release is built there.
#
# A release is built only from a clean working tree (commit the version bump first): the binaries carry the commit they were built from
# (informational version "<version>+<commit>"), so that commit must hold exactly the code that was built. Tag that commit.
set -eu
compress=true
ready_to_run=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --no-compression) compress=false; shift ;;
    --ready-to-run) ready_to_run=-p:PublishReadyToRun=true; shift ;;
    *) break ;;
  esac
done
root=$(cd "$(dirname "$0")/.." && pwd)
if [ -n "$(git -C "$root" status --porcelain)" ]; then
  echo "The working tree has uncommitted changes; commit them first (the binaries must match their commit):" >&2
  git -C "$root" status --porcelain >&2
  exit 1
fi
commit=$(git -C "$root" rev-parse HEAD)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)
release_dir=${RELEASE_DIR:-artifacts/release-$version}
publish_dir=${PUBLISH_DIR:-artifacts/publish-$version}
case "$release_dir" in /*) ;; *) release_dir="$root/$release_dir" ;; esac
case "$publish_dir" in /*) ;; *) publish_dir="$root/$publish_dir" ;; esac
echo "Building $version from commit $commit into $release_dir"
[ "$#" -gt 0 ] || set -- win-x64 linux-x64 linux-arm64
for rid in "$@"; do
  out="$publish_dir/$rid"
  # Compressed inside the single file on every system (0.1.3, measured: since the trimmed build the compressed and the uncompressed file
  # use the same memory within 2-5 MB, and the compressed one is half the size on disk). Without --ready-to-run its property is not passed.
  dotnet publish "$root/src/TMConcept.Desktop/TMConcept.Desktop.csproj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile="$compress" -p:DebugType=none -p:DebugSymbols=false \
    -p:SourceRevisionId="$commit" ${ready_to_run:+"$ready_to_run"} -m:1 -nodeReuse:false -o "$out"
  python3 "$root/tools/package.py" "$out" "$rid" "$version" "$release_dir"
done
if [ -n "$(git -C "$root" status --porcelain)" ]; then
  echo "The build changed tracked files; the binaries may not match commit $commit." >&2
  exit 1
fi
