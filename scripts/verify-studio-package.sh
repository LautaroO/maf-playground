#!/usr/bin/env sh
set -eu
repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
test -f "$repository_root/src/MafPlayground.Studio.Web/dist/studio/browser/index.html" || { echo 'Run ./scripts/build-studio.sh first.' >&2; exit 1; }
publish=$(mktemp -d "${TMPDIR:-/tmp}/studio-publish.XXXXXX")
trap 'rm -rf "$publish"' EXIT HUP INT TERM
dotnet publish "$repository_root/src/MafPlayground.CLI" -c Release -o "$publish" -m:1 -p:UseSharedCompilation=false -nr:false
STUDIO_PUBLISH_DIRECTORY="$publish" dotnet test "$repository_root/tests/MafPlayground.Studio.IntegrationTests" --filter Category=StudioPackaging -m:1 -p:UseSharedCompilation=false -nr:false
