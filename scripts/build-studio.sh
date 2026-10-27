#!/usr/bin/env sh
set -eu
repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
web="$repository_root/src/MafPlayground.Studio.Web"
npm --prefix "$web" ci
npm --prefix "$web" run build
# Licenses must accompany the offline font and codicon assets.
test -f "$web/dist/studio/browser/licenses/DM-Sans-OFL.txt"
test -f "$web/dist/studio/browser/licenses/IBM-Plex-Mono-OFL.txt"
test -f "$web/dist/studio/browser/licenses/Monaco-LICENSE"
test -f "$web/dist/studio/browser/licenses/Monaco-ThirdPartyNotices.txt"
test -f "$web/dist/studio/browser/licenses/Codicons-CC-BY-4.0.txt"
test -f "$web/dist/studio/browser/licenses/Codicons-NOTICE.txt"
test -f "$web/dist/studio/browser/licenses/ThirdParty-LICENSES.txt"
