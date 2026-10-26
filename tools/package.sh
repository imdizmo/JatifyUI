#!/bin/sh
# Build the plugin and the Thunderstore zip: dist/Jatify-JatifyUI-<version>.zip
# The README is the repo's own (the same page Thunderstore shows); the loading screen art goes beside the DLL.
set -e
R="$(cd "$(dirname "$0")/.." && pwd)"
V=$(python3 -c "import json;print(json.load(open('$R/package/manifest.json'))['version_number'])")
dotnet build "$R/JatifyUI.csproj" -c Release -v minimal -o "$R/build" -p:ValheimDir="${VALHEIM:-/mnt/games/SteamLibrary/steamapps/common/Valheim}" "$@" >/dev/null
S=$(mktemp -d); trap 'rm -rf "$S"' EXIT
mkdir -p "$S/plugins" "$R/dist"
cp "$R/package/manifest.json" "$R/package/icon.png" "$R/package/CHANGELOG.md" "$S/"
cp "$R/README.md" "$S/README.md"
# a release never carries the dev tools (they only build with -p:JatifyDev=true): refuse if any slipped in
if strings -e l "$R/build/JatifyUI.dll" | grep -q -E "Give test kit|shot\.request" || strings "$R/build/JatifyUI.dll" | grep -q -E "^(DevTest|TestPanel|DevShot|FlatWorld|DevDump)$"; then
  echo "package: the DLL contains dev tools, not packaging (build without -p:JatifyDev=true)" >&2; exit 1
fi
cp "$R/build/JatifyUI.dll" "$S/plugins/"
cp "$R/LICENSE" "$S/"
cp -r "$R/package/loading" "$S/plugins/campfire-loading"   # the loading screen art (Campfire/LoadingArt looks beside the DLL)
rm -f "$R/dist/Jatify-JatifyUI-$V.zip"
(cd "$S" && zip -qr -X "$R/dist/Jatify-JatifyUI-$V.zip" .)
echo "$R/dist/Jatify-JatifyUI-$V.zip"
