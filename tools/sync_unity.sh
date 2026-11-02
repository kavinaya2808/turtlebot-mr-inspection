#!/usr/bin/env bash
# Copy the Unity part of this project out of the host Unity project into unity/, sanitised for
# a public repository (private IPs -> 127.0.0.1, lab robot host names -> "turtlebot3").
#
#   tools/sync_unity.sh ~/MR-SLAM
#
# Run it whenever you have changed the Unity side, then commit.
set -euo pipefail

HOST="${1:?usage: tools/sync_unity.sh /path/to/host/unity/project}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$HOST/Assets/RealTurtleBotMR"
DST="$REPO/unity/RealTurtleBotMR"

[ -d "$SRC" ] || { echo "Not found: $SRC"; exit 1; }
mkdir -p "$DST"

rsync -a --delete \
  --exclude 'ros~' --exclude 'ros~.meta' --exclude '.DS_Store' --exclude '.git' \
  "$SRC/" "$DST/"
[ -f "$HOST/Assets/RealTurtleBotMR.meta" ] && cp "$HOST/Assets/RealTurtleBotMR.meta" "$REPO/unity/"

# Sanitise text assets (C#, scenes, prefabs, Unity assets, docs).
find "$DST" -type f \( -name '*.cs' -o -name '*.unity' -o -name '*.prefab' -o -name '*.asset' \
  -o -name '*.md' -o -name '*.sh' \) -print0 |
  xargs -0 perl -pi -e '
    s/\b192\.168(?:\.\d{1,3}){2}\b/127.0.0.1/g;
    s/\b10(?:\.\d{1,3}){3}\b/127.0.0.1/g;
    s/\b172\.(?:1[6-9]|2\d|3[01])(?:\.\d{1,3}){2}\b/127.0.0.1/g;
    s/\btbot3-\d+\b/turtlebot3/g;
  '

echo "Synced $SRC -> $DST"
if grep -rnE '\b192\.168\.[0-9]+\.[0-9]+|\btbot3-[0-9]|password' "$DST" \
    --include='*.cs' --include='*.unity' --include='*.md' --include='*.asset'; then
  echo "!! Check the lines above before committing."
  exit 2
fi
echo "No private IPs, robot host names or passwords found."
