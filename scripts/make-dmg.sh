#!/bin/zsh
# Packages dist/UsageMeter.app as a drag-to-install disk image.
#
# Everything the image needs is staged on disk first and the image is created
# from that staging folder, so the only thing done to the mounted volume is the
# Finder arrangement — which is best-effort: on a machine that denies Finder
# automation (CI, most notably) the image is still produced, just with the
# default list view instead of the laid-out window.
#
#   ./scripts/make-dmg.sh [--no-build]
set -euo pipefail

USAGEMETER_SCRIPT_DIR=${0:A:h}
USAGEMETER_ROOT=${USAGEMETER_SCRIPT_DIR:h}
USAGEMETER_APP="$USAGEMETER_ROOT/dist/UsageMeter.app"
USAGEMETER_STAGE="$USAGEMETER_ROOT/.build/dmg-stage"
USAGEMETER_TEMP_DMG="$USAGEMETER_ROOT/.build/UsageMeter-rw.dmg"
USAGEMETER_VOLUME="UsageMeter"
USAGEMETER_BUILD=1

for USAGEMETER_ARG in "$@"; do
  case "$USAGEMETER_ARG" in
    --no-build) USAGEMETER_BUILD=0 ;;
    *) echo "Unknown option: $USAGEMETER_ARG" >&2; exit 2 ;;
  esac
done

if (( USAGEMETER_BUILD )) || [[ ! -d "$USAGEMETER_APP" ]]; then
  "$USAGEMETER_ROOT/scripts/build-app.sh"
fi

USAGEMETER_VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$USAGEMETER_APP/Contents/Info.plist")
USAGEMETER_DMG="$USAGEMETER_ROOT/dist/UsageMeter-$USAGEMETER_VERSION.dmg"

# Stage: the app, the alias people drop it on, the window background, and the
# icon the volume itself wears.
rm -rf "$USAGEMETER_STAGE" "$USAGEMETER_TEMP_DMG" "$USAGEMETER_DMG"
mkdir -p "$USAGEMETER_STAGE/.background" "$USAGEMETER_ROOT/dist"
cp -R "$USAGEMETER_APP" "$USAGEMETER_STAGE/UsageMeter.app"
ln -s /Applications "$USAGEMETER_STAGE/Applications"
/usr/bin/swift "$USAGEMETER_ROOT/scripts/make-dmg-background.swift" "$USAGEMETER_STAGE/.background/background.tiff"

# A read-write image first, because the Finder arrangement and the volume icon
# have to be written into the volume before it is compressed and sealed. The
# slack is for those two: `-srcfolder` alone sizes the image to its contents.
USAGEMETER_SIZE=$(( $(/usr/bin/du -sm "$USAGEMETER_STAGE" | awk '{ print $1 }') + 24 ))
/usr/bin/hdiutil create \
  -volname "$USAGEMETER_VOLUME" \
  -srcfolder "$USAGEMETER_STAGE" \
  -fs HFS+ \
  -format UDRW \
  -size "${USAGEMETER_SIZE}m" \
  -ov \
  "$USAGEMETER_TEMP_DMG" >/dev/null

USAGEMETER_MOUNT=$(/usr/bin/hdiutil attach "$USAGEMETER_TEMP_DMG" -nobrowse -noverify -noautoopen | awk -F'\t' '/\/Volumes\// { print $NF }' | tail -1)
USAGEMETER_MOUNTED_NAME=${USAGEMETER_MOUNT:t}
echo "Mounted $USAGEMETER_MOUNT"

usagemeter_detach() {
  for _ in 1 2 3 4 5; do
    /usr/bin/hdiutil detach "$USAGEMETER_MOUNT" -quiet && return 0
    sleep 1
  done
  /usr/bin/hdiutil detach "$USAGEMETER_MOUNT" -force -quiet || true
}
trap usagemeter_detach EXIT INT TERM

# Best-effort: the window layout needs Finder automation, which CI does not
# grant. The image is still valid without it, just not laid out.
/usr/bin/osascript - "$USAGEMETER_MOUNTED_NAME" <<'APPLESCRIPT' || echo "note: Finder would not arrange the window; the image is still valid"
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
      set position of item "UsageMeter.app" of container window to {160, 230}
      set position of item "Applications" of container window to {480, 230}
      update without registering applications
      close
    end tell
  end tell
end run
APPLESCRIPT

# The volume icon goes on last. It is written here rather than staged because
# `hdiutil create -srcfolder` does not carry `.VolumeIcon.icns` into the image,
# and it goes after the arrangement because Finder removes the file when it
# opens a volume that is already flagged as having a custom icon. The flag
# itself needs Xcode's SetFile, so a Command Line Tools-only machine simply
# ships the generic volume icon.
cp "$USAGEMETER_APP/Contents/Resources/AppIcon.icns" "$USAGEMETER_MOUNT/.VolumeIcon.icns"
if [[ -x /usr/bin/SetFile ]]; then
  /usr/bin/SetFile -a C "$USAGEMETER_MOUNT" || echo "note: could not flag the volume icon"
else
  echo "note: SetFile is unavailable, so the volume keeps the generic icon"
fi

sync
usagemeter_detach
trap - EXIT INT TERM

/usr/bin/hdiutil convert "$USAGEMETER_TEMP_DMG" -format UDZO -imagekey zlib-level=9 -o "$USAGEMETER_DMG" >/dev/null
rm -f "$USAGEMETER_TEMP_DMG"
rm -rf "$USAGEMETER_STAGE"

/usr/bin/hdiutil verify "$USAGEMETER_DMG" >/dev/null
echo "Built $USAGEMETER_DMG"
/usr/bin/shasum -a 256 "$USAGEMETER_DMG"
/bin/ls -lh "$USAGEMETER_DMG" | awk '{ print $5 }'
