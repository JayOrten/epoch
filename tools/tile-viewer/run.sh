#!/usr/bin/env bash
# Starts the tile viewer — serves from repo root and opens a browser.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PORT="${1:-8080}"
URL="http://localhost:$PORT/tools/tile-viewer/"

cd "$REPO_ROOT" || exit 1

# Try to open browser (background, non-blocking)
if command -v xdg-open &>/dev/null; then
  (sleep 0.5 && xdg-open "$URL") &
elif command -v open &>/dev/null; then
  (sleep 0.5 && open "$URL") &
fi

echo "Tile Viewer: $URL"
echo "Press Ctrl+C to stop."
python3 "$SCRIPT_DIR/server.py" "$PORT"
