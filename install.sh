#!/bin/sh
set -e

# GdbMiBridge.Mcp — self-contained binary installer
# Usage: curl -fsSL https://raw.githubusercontent.com/gang2k-coder/GdbMiClient/main/install.sh | bash
#        sh install.sh [version]

REPO="gang2k-coder/GdbMiClient"
VERSION="${1:-latest}"

# ---- platform detection ----
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Linux)
    case "$ARCH" in
      x86_64)  RID="linux-x64" ;;
      aarch64) RID="linux-arm64" ;;
      arm64)   RID="linux-arm64" ;;
      *)
        echo "Unsupported architecture: $ARCH" >&2
        exit 1
        ;;
    esac
    ASSET="GdbMiBridge.Mcp-${RID}"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    RID="win-x64"
    ASSET="GdbMiBridge.Mcp-${RID}.exe"
    ;;
  *)
    echo "Unsupported OS: $OS" >&2
    echo "This installer supports Linux and Windows (MSYS2/Git Bash/WSL)." >&2
    exit 1
    ;;
esac

# ---- resolve download URLs ----
if [ "$VERSION" = "latest" ]; then
  BIN_URL="https://github.com/${REPO}/releases/latest/download/${ASSET}"
  SHA_URL="https://github.com/${REPO}/releases/latest/download/${ASSET}.sha256"
else
  BIN_URL="https://github.com/${REPO}/releases/download/v${VERSION}/${ASSET}"
  SHA_URL="https://github.com/${REPO}/releases/download/v${VERSION}/${ASSET}.sha256"
fi

# ---- choose install directory ----
if [ -w /usr/local/bin ]; then
  INSTALL_DIR="/usr/local/bin"
elif [ -d "$HOME/.local/bin" ]; then
  INSTALL_DIR="$HOME/.local/bin"
else
  INSTALL_DIR="$HOME/.local/bin"
  mkdir -p "$INSTALL_DIR"
fi

TARGET="${INSTALL_DIR}/GdbMiBridge"

# ---- determine download tool ----
if command -v curl >/dev/null 2>&1; then
  download() { curl -fsSL "$1" -o "$2"; }
elif command -v wget >/dev/null 2>&1; then
  download() { wget -q "$1" -O "$2"; }
else
  echo "Neither curl nor wget found. Please install one of them." >&2
  exit 1
fi

# ---- download binary ----
echo "Downloading GdbMiBridge (${RID})..."
TMP_BIN="$(mktemp)"
download "$BIN_URL" "$TMP_BIN"
chmod +x "$TMP_BIN"

# ---- verify checksum (best effort) ----
TMP_SHA="$(mktemp)"
if download "$SHA_URL" "$TMP_SHA" 2>/dev/null; then
  EXPECTED="$(awk '{print $1}' "$TMP_SHA")"
  if [ -n "$EXPECTED" ] && command -v sha256sum >/dev/null 2>&1; then
    ACTUAL="$(sha256sum "$TMP_BIN" | awk '{print $1}')"
    if [ "$EXPECTED" = "$ACTUAL" ]; then
      echo "Checksum verified OK."
    else
      echo "WARNING: checksum mismatch. Expected: $EXPECTED  Got: $ACTUAL" >&2
      rm -f "$TMP_BIN" "$TMP_SHA"
      exit 1
    fi
  elif [ -n "$EXPECTED" ] && command -v shasum >/dev/null 2>&1; then
    ACTUAL="$(shasum -a 256 "$TMP_BIN" | awk '{print $1}')"
    if [ "$EXPECTED" = "$ACTUAL" ]; then
      echo "Checksum verified OK."
    else
      echo "WARNING: checksum mismatch. Expected: $EXPECTED  Got: $ACTUAL" >&2
      rm -f "$TMP_BIN" "$TMP_SHA"
      exit 1
    fi
  fi
fi
rm -f "$TMP_SHA"

# ---- install ----
echo "Installing to ${TARGET}..."
mv "$TMP_BIN" "$TARGET"

# ---- check PATH ----
case ":$PATH:" in
  *":${INSTALL_DIR}:"*) ;;
  *)
    echo ""
    echo "NOTE: ${INSTALL_DIR} is not on your PATH."
    echo "Add the following to your shell profile:"
    echo ""
    echo "    export PATH=\"${INSTALL_DIR}:\$PATH\""
    echo ""
    ;;
esac

echo ""
echo "GdbMiBridge installed successfully!"
echo ""
echo "========================================"
echo "  How to configure MCP"
echo "========================================"
echo ""
echo "Claude Code (project-level) — create .mcp.json at your project root:"
echo ""
echo '  {'
echo '    "mcpServers": {'
echo '      "gdb-debug": {'
echo '        "type": "stdio",'
echo '        "command": "GdbMiBridge"'
echo '      }'
echo '    }'
echo '  }'
echo ""
echo "Claude Code (global) — add to ~/.claude.json under \"mcpServers\":"
echo ""
echo '  "gdb-debug": {'
echo '    "type": "stdio",'
echo '    "command": "GdbMiBridge"'
echo '  }'
echo ""
echo "Cursor — create .cursor/mcp.json at your project root:"
echo ""
echo '  {'
echo '    "mcpServers": {'
echo '      "gdb-debug": {'
echo '        "type": "stdio",'
echo '        "command": "GdbMiBridge"'
echo '      }'
echo '    }'
echo '  }'
echo ""
echo "After configuring, restart Claude Code / Cursor and use /mcp to verify."
