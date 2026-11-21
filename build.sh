#!/bin/bash
# Builds build/Snapmark.app and build/Snapmark.dmg
set -euo pipefail
cd "$(dirname "$0")"

APP=build/Snapmark.app
rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

swiftc -O -swift-version 5 -target arm64-apple-macos14 Sources/main.swift -o "$APP/Contents/MacOS/Snapmark"
cp Info.plist "$APP/Contents/"

# App icon
swift Icon/make_icon.swift build/AppIcon.iconset
iconutil -c icns build/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf build/AppIcon.iconset

# Sign with a stable identity so the Screen Recording permission survives rebuilds.
ID=$(security find-identity -v -p codesigning | awk '/Apple Development/ {print $2; exit}')
codesign --force --sign "${ID:--}" "$APP"

STAGE=build/dmg
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -quiet -volname Snapmark -srcfolder "$STAGE" -ov -format UDZO build/Snapmark.dmg
rm -rf "$STAGE"

# Install: quit the running app, remove every other Snapmark copy, put the new one in /Applications.
pkill -x Snapmark || true
sleep 1
mdfind "kMDItemCFBundleIdentifier == 'local.snapmark'" | while read -r old; do
    if [[ "$old" == *.app && "$old" != "$PWD/$APP" ]]; then rm -rf "$old"; echo "Removed $old"; fi
done
rm -rf /Applications/Snapmark.app
mv "$APP" /Applications/
open /Applications/Snapmark.app

echo "Installed /Applications/Snapmark.app (DMG: build/Snapmark.dmg)"
