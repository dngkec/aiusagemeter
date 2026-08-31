#!/bin/zsh
# Packages dist/AIUsageMeter.app as a drag-to-install disk image.
#
#   ./scripts/make-dmg.sh [--no-build]
set -euo pipefail

AIUSAGEMETER_SCRIPT_DIR=${0:A:h}
AIUSAGEMETER_ROOT=${AIUSAGEMETER_SCRIPT_DIR:h}
AIUSAGEMETER_APP="$AIUSAGEMETER_ROOT/dist/AIUsageMeter.app"
AIUSAGEMETER_STAGE="$AIUSAGEMETER_ROOT/.build/dmg-stage"
AIUSAGEMETER_TEMP_DMG="$AIUSAGEMETER_ROOT/.build/AIUsageMeter-rw.dmg"
AIUSAGEMETER_VOLUME="AIUsageMeter"
AIUSAGEMETER_BUILD=1

for AIUSAGEMETER_ARG in "$@"; do
  case "$AIUSAGEMETER_ARG" in
    --no-build) AIUSAGEMETER_BUILD=0 ;;
    *) echo "Unknown option: $AIUSAGEMETER_ARG" >&2; exit 2 ;;
  esac
done

if (( AIUSAGEMETER_BUILD )) || [[ ! -d "$AIUSAGEMETER_APP" ]]; then
  "$AIUSAGEMETER_ROOT/scripts/build-app.sh"
fi

AIUSAGEMETER_VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$AIUSAGEMETER_APP/Contents/Info.plist")
AIUSAGEMETER_DMG="$AIUSAGEMETER_ROOT/dist/AIUsageMeter-$AIUSAGEMETER_VERSION.dmg"

# Stage the app, the Applications alias, the background, and the volume icon.
rm -rf "$AIUSAGEMETER_STAGE" "$AIUSAGEMETER_TEMP_DMG" "$AIUSAGEMETER_DMG"
mkdir -p "$AIUSAGEMETER_STAGE/.background" "$AIUSAGEMETER_ROOT/dist"
cp -R "$AIUSAGEMETER_APP" "$AIUSAGEMETER_STAGE/AIUsageMeter.app"
ln -s /Applications "$AIUSAGEMETER_STAGE/Applications"
/usr/bin/swift "$AIUSAGEMETER_ROOT/scripts/make-dmg-background.swift" "$AIUSAGEMETER_STAGE/.background/background.tiff"

# Read-write first: the arrangement and volume icon are written before sealing,
# and `-srcfolder` alone would size the image to its contents with no slack.
AIUSAGEMETER_SIZE=$(( $(/usr/bin/du -sm "$AIUSAGEMETER_STAGE" | awk '{ print $1 }') + 24 ))
/usr/bin/hdiutil create \
  -volname "$AIUSAGEMETER_VOLUME" \
  -srcfolder "$AIUSAGEMETER_STAGE" \
  -fs HFS+ \
  -format UDRW \
  -size "${AIUSAGEMETER_SIZE}m" \
  -ov \
  "$AIUSAGEMETER_TEMP_DMG" >/dev/null

AIUSAGEMETER_MOUNT=$(/usr/bin/hdiutil attach "$AIUSAGEMETER_TEMP_DMG" -nobrowse -noverify -noautoopen | awk -F'\t' '/\/Volumes\// { print $NF }' | tail -1)
AIUSAGEMETER_MOUNTED_NAME=${AIUSAGEMETER_MOUNT:t}
echo "Mounted $AIUSAGEMETER_MOUNT"

aiusagemeter_detach() {
  for _ in 1 2 3 4 5; do
    /usr/bin/hdiutil detach "$AIUSAGEMETER_MOUNT" -quiet && return 0
    sleep 1
  done
  /usr/bin/hdiutil detach "$AIUSAGEMETER_MOUNT" -force -quiet || true
}
trap aiusagemeter_detach EXIT INT TERM

# Best-effort: the layout needs Finder automation, which CI does not grant.
/usr/bin/osascript - "$AIUSAGEMETER_MOUNTED_NAME" <<'APPLESCRIPT' || echo "note: Finder would not arrange the window; the image is still valid"
on run argv
  set volumeName to item 1 of argv
  tell application "Finder"
    tell disk volumeName
      open
      set current view of container window to icon view
      set toolbar visible of container window to false
      set statusbar visible of container window to false
      set the bounds of container window to {200, 120, 840, 520}
      set viewOptions to the icon view options of container window
      set arrangement of viewOptions to not arranged
      set icon size of viewOptions to 112
      set text size of viewOptions to 12
      set background picture of viewOptions to file ".background:background.tiff"
      set position of item "AIUsageMeter.app" of container window to {160, 230}
      set position of item "Applications" of container window to {480, 230}
      update without registering applications
      close
    end tell
  end tell
end run
APPLESCRIPT

# Last: `-srcfolder` does not carry `.VolumeIcon.icns` in, and Finder deletes it
# when opening a volume already flagged for a custom icon. The flag needs SetFile.
cp "$AIUSAGEMETER_APP/Contents/Resources/AppIcon.icns" "$AIUSAGEMETER_MOUNT/.VolumeIcon.icns"
if [[ -x /usr/bin/SetFile ]]; then
  /usr/bin/SetFile -a C "$AIUSAGEMETER_MOUNT" || echo "note: could not flag the volume icon"
else
  echo "note: SetFile is unavailable, so the volume keeps the generic icon"
fi

sync
aiusagemeter_detach
trap - EXIT INT TERM

/usr/bin/hdiutil convert "$AIUSAGEMETER_TEMP_DMG" -format UDZO -imagekey zlib-level=9 -o "$AIUSAGEMETER_DMG" >/dev/null
rm -f "$AIUSAGEMETER_TEMP_DMG"
rm -rf "$AIUSAGEMETER_STAGE"

/usr/bin/hdiutil verify "$AIUSAGEMETER_DMG" >/dev/null
echo "Built $AIUSAGEMETER_DMG"
/usr/bin/shasum -a 256 "$AIUSAGEMETER_DMG"
/bin/ls -lh "$AIUSAGEMETER_DMG" | awk '{ print $5 }'
