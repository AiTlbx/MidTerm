#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

echo "Building MiddleManager AOT for macOS ($RID)..."
dotnet publish -c Release -r "$RID" /p:IsPublishing=true

# Build native PTY helper
echo "Building PTY helper..."
clang -O2 "$SCRIPT_DIR/Pty/pty_helper.c" -o "bin/Release/net10.0/$RID/publish/pty_helper"

echo ""
echo "Build complete!"
echo "Output: bin/Release/net10.0/$RID/publish/"
ls -lh "bin/Release/net10.0/$RID/publish/mm" 2>/dev/null || echo "(file not found - check build output)"
ls -lh "bin/Release/net10.0/$RID/publish/pty_helper" 2>/dev/null || echo "(pty_helper not found)"
