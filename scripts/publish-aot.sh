#!/usr/bin/env bash
set -euo pipefail

# NativeAOT publisher for CircuitPy Watcher
# - Builds trimmed, self-contained binaries for one or more RIDs
# - Removes symbol files (.dbg/.pdb) and optionally strips binaries
#
# Usage:
#   scripts/publish-aot.sh                 # linux-x64, osx-arm64, win-x64
#   scripts/publish-aot.sh --rid linux-x64 # single RID
#   scripts/publish-aot.sh linux-x64 win-x64
#   scripts/publish-aot.sh --no-strip      # keep symbols, don't strip

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT_DIR/watcher/watcher.csproj"
TFM="net10.0"
NO_STRIP=""
RIDS=()

while (($#)); do
  case "$1" in
    --rid)
      shift
      RIDS+=("${1:-}")
      ;;
    --rid=*)
      RIDS+=("${1#--rid=}")
      ;;
    --no-strip)
      NO_STRIP=1
      ;;
    -h|--help)
      sed -n '1,100p' "$0" | sed -n '1,30p'
      exit 0
      ;;
    *)
      RIDS+=("$1")
      ;;
  esac
  shift || true
done

if [ ${#RIDS[@]} -eq 0 ]; then
  RIDS=(linux-x64 osx-arm64 win-x64)
fi

echo "Publishing AOT for: ${RIDS[*]}" >&2

for RID in "${RIDS[@]}"; do
  echo "==> dotnet publish ($RID)" >&2
  dotnet publish "$PROJ" -c Release -r "$RID" -p:PublishAot=true --nologo

  PUBDIR="$ROOT_DIR/watcher/bin/Release/$TFM/$RID/publish"
  if [ ! -d "$PUBDIR" ]; then
    echo "Publish folder not found: $PUBDIR" >&2
    exit 1
  fi

  # Remove symbols
  rm -f "$PUBDIR"/*.dbg "$PUBDIR"/*.pdb || true

  # Optional strip for extra size savings (if available)
  if [ -z "$NO_STRIP" ] && command -v strip >/dev/null 2>&1; then
    BIN="$PUBDIR/watcher"
    if [ -f "$BIN.exe" ]; then BIN="$BIN.exe"; fi
    echo "Stripping $BIN" >&2
    strip -s "$BIN" || true
  fi

  # Report size
  if [[ "$RID" == win* ]]; then BN="watcher.exe"; else BN="watcher"; fi
  if [ -f "$PUBDIR/$BN" ]; then
    printf "RID: %s  size: %s\n" "$RID" "$(du -h "$PUBDIR/$BN" | awk '{print $1}')"
  else
    echo "Binary not found in $PUBDIR" >&2
  fi
done

echo "Done." >&2

