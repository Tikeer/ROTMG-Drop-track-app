#!/usr/bin/env bash
set -euo pipefail

# Builds a clickable macOS .app bundle ("RotMG Tracker.app") with a single-file binary.
# Usage: ./scripts/build-macos.sh [osx-arm64|osx-x64]

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

ARCH="${1:-}"
if [ -z "$ARCH" ]; then
    if [ "$(uname -m)" = "arm64" ]; then
        ARCH="osx-arm64"
    else
        ARCH="osx-x64"
    fi
fi

APP_BUNDLE="$REPO_ROOT/publish/RotMG Tracker.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"

echo "Publishing single-file macOS build ($ARCH)..."
rm -rf "$APP_BUNDLE"
mkdir -p "$MACOS_DIR"

dotnet publish "$REPO_ROOT/RotmgTracker.Desktop/RotmgTracker.Desktop.csproj" \
    -c Release \
    -r "$ARCH" \
    --self-contained true \
    -p:AssemblyName=RotmgTracker \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o "$MACOS_DIR"

cat > "$CONTENTS_DIR/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>RotMG Tracker</string>
    <key>CFBundleExecutable</key>
    <string>RotmgTracker</string>
    <key>CFBundleIdentifier</key>
    <string>com.tikeer.rotmgtracker</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>RotMG Tracker</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>2.0.0</string>
    <key>CFBundleVersion</key>
    <string>2.0.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

chmod +x "$MACOS_DIR/RotmgTracker"

echo ""
echo "Build complete!"
echo "  App bundle: $APP_BUNDLE"
echo "  You can double-click it in Finder or drag it into /Applications."
