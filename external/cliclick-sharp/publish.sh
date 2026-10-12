#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/src/CliclickSharp"
DIST="$ROOT/dist"

# The shipped binary is universal (x86_64 + arm64): the DevFlow agent package runs it on both
# Apple silicon and Intel Macs. An arm64-only build silently breaks Intel hosts.
for RID in osx-arm64 osx-x64; do
  echo "=== Publishing CliclickSharp AOT for $RID ==="
  dotnet publish "$SRC/CliclickSharp.csproj" \
    -c Release \
    -r "$RID" \
    -p:PublishAot=true \
    -p:OutputType=Exe \
    --self-contained \
    -o "$DIST/$RID"
  echo ""
done

echo "=== Creating universal binary ==="
mkdir -p "$DIST"
lipo -create "$DIST/osx-arm64/CliclickSharp" "$DIST/osx-x64/CliclickSharp" -output "$DIST/CliclickSharp"

echo "=== Cleaning up per-arch artifacts ==="
rm -rf "$DIST/osx-arm64" "$DIST/osx-x64"

echo ""
echo "=== Done ==="
echo "universal binary: $DIST/CliclickSharp"
file "$DIST/CliclickSharp"
echo ""
echo "Size: $(du -h "$DIST/CliclickSharp" | cut -f1)"
