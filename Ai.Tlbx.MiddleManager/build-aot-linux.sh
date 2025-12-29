#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Building MiddleManager AOT for Linux x64..."
dotnet publish -c Release -r linux-x64 /p:IsPublishing=true

echo ""
echo "Build complete!"
echo "Output: bin/Release/net10.0/linux-x64/publish/"
ls -lh bin/Release/net10.0/linux-x64/publish/mm 2>/dev/null || echo "(file not found - check build output)"
