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

echo "Building MidTerm AOT for macOS ($RID)..."
dotnet publish -c Release -r "$RID" /p:IsPublishing=true

echo ""
echo "Build complete!"
echo "Output: bin/Release/net10.0/$RID/publish/"
ls -lh "bin/Release/net10.0/$RID/publish/mt" 2>/dev/null || echo "(file not found - check build output)"
