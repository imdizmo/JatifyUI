#!/usr/bin/env bash
# Build and drop the DLL into the dev profile's ScriptEngine folder. ScriptEngine watches that folder
# and reloads the plugin in the running game about a second later. The copy goes to a temporary name
# and is renamed into place, so the watcher never reads a half-written file.
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"
VALHEIM="${VALHEIM:-/mnt/games/SteamLibrary/steamapps/common/Valheim}"
PROFILE="${PROFILE:-$HOME/.local/share/com.kesomannen.gale/valheim/profiles/jatify-ui-dev}"
DEST="$PROFILE/BepInEx/scripts"
dotnet build "$R/JatifyUI.csproj" -c Release -v minimal -o "$R/build" -p:ValheimDir="$VALHEIM" -p:ProfileDir="$PROFILE" -p:JatifyDev=true | grep -E "error|warn|Build succeeded" | sort -u
mkdir -p "$DEST"
# ScriptEngine reads symbols and refuses a DLL without its .pdb, so the .pdb goes first; the watcher
# only looks at *.dll, so the DLL landing is what triggers the reload.
cp "$R/build/JatifyUI.pdb" "$DEST/.JatifyUI.pdb.tmp" && mv -f "$DEST/.JatifyUI.pdb.tmp" "$DEST/JatifyUI.pdb"
cp "$R/build/JatifyUI.dll" "$DEST/.JatifyUI.tmp"
mv -f "$DEST/.JatifyUI.tmp" "$DEST/JatifyUI.dll"
mkdir -p "$PROFILE/BepInEx/plugins/campfire-loading" && cp -u "$R/package/loading/"*.jpg "$PROFILE/BepInEx/plugins/campfire-loading/"   # Campfire's loading screen art (read from disk, not reloaded)
echo "dropped into $DEST (reloads in-game if the dev profile is running)"
