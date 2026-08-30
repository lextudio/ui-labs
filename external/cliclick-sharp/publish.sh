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
echo "=== Staging arm64 binary ==="
mkdir -p "$DIST"
cp "$DIST/osx-arm64/CliclickSharp" "$DIST/CliclickSharp"

echo "=== Cleaning up per-arch artifact ==="
rm -rf "$DIST/osx-arm64"

echo ""
echo "=== Done ==="
echo "arm64 binary: $DIST/CliclickSharp"
file "$DIST/CliclickSharp"
echo ""
echo "Size: $(du -h "$DIST/CliclickSharp" | cut -f1)"
