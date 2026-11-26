#!/usr/bin/env bash
# Generates Meshopt-compressed web-tier GLBs for ScadaWeb from the master Exported/*.glb.
# Masters are NEVER modified (ArchScada.Desktop's SharpGLTF cannot decode EXT_meshopt_compression).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SRC_DIR="$ROOT/ArchScada/Assets/3D/Exported"
DST_DIR="$ROOT/ArchScada/Assets/3D/ExportedWeb"
mkdir -p "$DST_DIR"
count=0
for src in "$SRC_DIR"/*.glb; do
  dst="$DST_DIR/$(basename "$src")"
  gltf-transform optimize "$src" "$dst" --compress meshopt --texture-compress false >/dev/null
  count=$((count + 1))
done
echo "optimize-web-glbs: wrote $count Meshopt GLBs to $DST_DIR"
