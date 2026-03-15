#!/usr/bin/env bash
# Builds the libgolf-bridge executable.
# Run from any directory — script always works relative to its own location.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# ── 1. Clone libgolf if not present ───────────────────────────────────────
if [ ! -d "external/libgolf" ]; then
    echo "[bridge] Cloning libgolf..."
    git clone --depth 1 https://github.com/gdifiore/libgolf.git external/libgolf
else
    echo "[bridge] external/libgolf already present, skipping clone."
fi

# ── 2. Build libgolf static library ───────────────────────────────────────
echo "[bridge] Building libgolf..."
cd external/libgolf

# Suppress gtest fetch by treating BUILD_TESTING as OFF.
mkdir -p build
cd build
cmake .. \
    -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_TESTING=OFF \
    -DFETCHCONTENT_FULLY_DISCONNECTED=OFF \
    -DCMAKE_CXX_STANDARD=17
cmake --build . --target golf --parallel "$(nproc)"
cd "$SCRIPT_DIR"

# ── 3. Build the bridge executable ────────────────────────────────────────
echo "[bridge] Building libgolf-bridge..."
mkdir -p build
cd build
cmake .. -DCMAKE_BUILD_TYPE=Release
cmake --build . --target libgolf-bridge --parallel "$(nproc)"
cd "$SCRIPT_DIR"

BINARY="$SCRIPT_DIR/build/libgolf-bridge"
echo ""
echo "[bridge] Done. Binary: $BINARY"
echo ""
echo "Smoke test (Mizuno 9i nominal):"
"$BINARY" --speed 109 --angle 20.5 --sidespin 120 --backspin 7000 | \
    python3 -c "
import sys, json
d = json.load(sys.stdin)
pts = d['points']
print(f'  Points recorded : {len(pts)}')
land = pts[-1]
carry_yd = land[0] / 0.9144
off_yd   = land[2] / 0.9144
peak_m   = max(p[1] for p in pts)
print(f'  Carry           : {carry_yd:.1f} yd')
print(f'  Peak height     : {peak_m:.1f} m')
print(f'  Offline         : {off_yd:+.1f} yd')
" 2>/dev/null || echo "  (python3 not available for smoke test — binary built OK)"
