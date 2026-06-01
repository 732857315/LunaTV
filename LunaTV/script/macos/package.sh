#!/bin/bash
arch=$1
APP_NAME="./LunaTV.app"
PUBLISH_OUTPUT_DIRECTORY="../../LunaTV/bin/Release/net10.0/osx-$arch/publish/."

INFO_PLIST="./Info.plist"
ICON_FILE="./logo.icns"

if [ -d "$APP_NAME" ]; then
  rm -rf "$APP_NAME"
fi

mkdir "$APP_NAME"

mkdir "$APP_NAME/Contents"
mkdir "$APP_NAME/Contents/MacOS"
mkdir "$APP_NAME/Contents/Resources"

cp "$INFO_PLIST" "$APP_NAME/Contents/Info.plist"
cp "$ICON_FILE" "$APP_NAME/Contents/Resources/$ICON_FILE"
cp -a "$PUBLISH_OUTPUT_DIRECTORY" "$APP_NAME/Contents/MacOS"