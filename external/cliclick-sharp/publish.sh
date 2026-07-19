#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/src/CliclickSharp"
DIST="$ROOT/dist"

echo "=== Publishing CliclickSharp AOT for osx-arm64 ==="
dotnet publish "$SRC/CliclickSharp.csproj" \
  -c Release \
  -r osx-arm64 \
  -p:PublishAot=true \
  -p:OutputType=Exe \
  --self-contained \
  -o "$DIST/osx-arm64"

echo ""
echo "=== Publishing CliclickSharp AOT for osx-x64 ==="
dotnet publish "$SRC/CliclickSharp.csproj" \
  -c Release \
  -r osx-x64 \
  -p:PublishAot=true \
  -p:OutputType=Exe \
  --self-contained \
  -o "$DIST/osx-x64"

echo ""
echo "=== Creating universal binary with lipo ==="
mkdir -p "$DIST"
lipo -create \
  "$DIST/osx-arm64/CliclickSharp" \
  "$DIST/osx-x64/CliclickSharp" \
  -output "$DIST/CliclickSharp"

echo "=== Cleaning up per-arch artifacts ==="
rm -rf "$DIST/osx-arm64" "$DIST/osx-x64"

echo ""
echo "=== Done ==="
echo "Universal binary: $DIST/CliclickSharp"
file "$DIST/CliclickSharp"
echo ""
echo "Size: $(du -h "$DIST/CliclickSharp" | cut -f1)"
