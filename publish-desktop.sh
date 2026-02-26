#!/bin/bash
set -e

# BookOrganizer Desktop - macOS .app Bundle Publisher
# Produces BookOrganizer.app for drag-to-Applications installation

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="BookOrganizer.Desktop"
APP_NAME="BookOrganizer"
RUNTIME="osx-arm64"
CONFIG="Release"
PUBLISH_DIR="$SCRIPT_DIR/publish"
APP_BUNDLE="$PUBLISH_DIR/$APP_NAME.app"
DOTNET_OUT="$SCRIPT_DIR/$PROJECT/bin/$CONFIG/net10.0/$RUNTIME/publish"
ICON_SOURCE="$SCRIPT_DIR/$PROJECT/Assets/icon.png"

echo "Building $APP_NAME.app for $RUNTIME..."
echo ""

# --- Step 1: dotnet publish ---
echo "Publishing $PROJECT..."
dotnet publish "$SCRIPT_DIR/$PROJECT/$PROJECT.csproj" \
    -c "$CONFIG" \
    -r "$RUNTIME" \
    --self-contained \
    -p:PublishSingleFile=false
echo ""

# --- Step 2: Generate .icns from icon.png ---
echo "Generating icon.icns..."
ICONSET_DIR=$(mktemp -d)/icon.iconset
mkdir -p "$ICONSET_DIR"

for SIZE in 16 32 128 256 512; do
    sips -z $SIZE $SIZE "$ICON_SOURCE" --out "$ICONSET_DIR/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE=$((SIZE * 2))
    sips -z $DOUBLE $DOUBLE "$ICON_SOURCE" --out "$ICONSET_DIR/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done

ICNS_FILE="$PUBLISH_DIR/icon.icns"
mkdir -p "$PUBLISH_DIR"
iconutil --convert icns "$ICONSET_DIR" --output "$ICNS_FILE"
rm -rf "$(dirname "$ICONSET_DIR")"
echo ""

# --- Step 3: Create .app bundle ---
echo "Creating $APP_NAME.app bundle..."
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# Copy published binaries
cp -a "$DOTNET_OUT/"* "$APP_BUNDLE/Contents/MacOS/"

# Copy Info.plist and icon
cp "$SCRIPT_DIR/$PROJECT/Info.plist" "$APP_BUNDLE/Contents/"
cp "$ICNS_FILE" "$APP_BUNDLE/Contents/Resources/icon.icns"

# Ensure executable permission
chmod +x "$APP_BUNDLE/Contents/MacOS/$PROJECT"

# Clean up intermediate icns
rm -f "$ICNS_FILE"

# --- Done ---
echo ""
BUNDLE_SIZE=$(du -sh "$APP_BUNDLE" | cut -f1)
echo "Done: $APP_BUNDLE ($BUNDLE_SIZE)"
echo "Run: open \"$APP_BUNDLE\""
