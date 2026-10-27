#!/usr/bin/env bash
# Start Valheim with a Gale profile, the way Gale does: the profile's doorstop files go into the game folder, then Steam
# starts the game with that profile's BepInEx preloader. Gale doesn't need to be open.
#   tools/play.sh <profile>          refuses while Valheim is running
#   tools/play.sh <profile> --kill   closes the running game first (no save on quit: fine for test characters, may lose the
#                                    last minutes on a real one)
set -euo pipefail
P="${1:?usage: play.sh <profile> [--kill]}"
GALE="$HOME/.local/share/com.kesomannen.gale/valheim/profiles"
VALHEIM="${VALHEIM:-/mnt/games/SteamLibrary/steamapps/common/Valheim}"
DIR="$GALE/$P"
[ -d "$DIR/BepInEx/core" ] || { echo "no Gale profile '$P' in $GALE" >&2; exit 1; }

pid() { pgrep -f "$VALHEIM/valheim.x86_64" | head -1 || true; }
if [ -n "$(pid)" ]; then
  if [ "${2:-}" != "--kill" ]; then echo "Valheim is running (pid $(pid)): quit it, or pass --kill" >&2; exit 1; fi
  kill "$(pid)"; for _ in $(seq 1 20); do [ -z "$(pid)" ] && break; sleep 1; done
  [ -z "$(pid)" ] || { kill -9 "$(pid)"; sleep 2; }
  echo "closed the running game"
fi

# the loader files Gale copies (from the profile) into the game folder
for f in doorstop_libs .doorstop_version start_game_bepinex.sh start_server_bepinex.sh winhttp.dll; do
  [ -e "$DIR/$f" ] && cp -r "$DIR/$f" "$VALHEIM/"
done
setsid steam -applaunch 892970 --doorstop-enabled true --doorstop-target-assembly "$DIR/BepInEx/core/BepInEx.Preloader.dll" >/dev/null 2>&1 &
echo "starting Valheim with profile '$P' (log: $DIR/BepInEx/LogOutput.log)"
