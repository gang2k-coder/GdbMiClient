#!/bin/bash
# Build Hanoi Tower test program for GDB E2E tests.
# Requires: g++ or clang++
#
# Usage: ./build_hanoi.sh
#   Then: ./hanoi_linux [N]   (default N=5)

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

CXX="${CXX:-g++}"
echo "Using compiler: $CXX"

"$CXX" -g -O0 -o "$SCRIPT_DIR/hanoi_linux" "$SCRIPT_DIR/hanoi_linux.cpp"

echo "Build OK: $SCRIPT_DIR/hanoi_linux"
