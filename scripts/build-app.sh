#!/bin/zsh
set -euo pipefail

USAGEMETER_SCRIPT_DIR=${0:A:h}
USAGEMETER_ROOT=${USAGEMETER_SCRIPT_DIR:h}
USAGEMETER_SCRATCH="$USAGEMETER_ROOT/.build/package"
USAGEMETER_APP="$USAGEMETER_ROOT/dist/UsageMeter.app"
USAGEMETER_ICONSET="$USAGEMETER_ROOT/.build/AppIcon.iconset"

export CLANG_MODULE_CACHE_PATH="$USAGEMETER_ROOT/.build/clang-cache"
export SWIFTPM_MODULECACHE_OVERRIDE="$USAGEMETER_ROOT/.build/swiftpm-cache"

mkdir -p "$USAGEMETER_ROOT/.build" "$USAGEMETER_ROOT/dist"
USAGEMETER_BIN_DIR=$(swift build --package-path "$USAGEMETER_ROOT" --scratch-path "$USAGEMETER_SCRATCH" -c release --show-bin-path)
swift build --package-path "$USAGEMETER_ROOT" --scratch-path "$USAGEMETER_SCRATCH" -c release --product UsageMeter -Xswiftc -warnings-as-errors

rm -rf "$USAGEMETER_APP" "$USAGEMETER_ICONSET"
mkdir -p "$USAGEMETER_APP/Contents/MacOS" "$USAGEMETER_APP/Contents/Resources"
cp "$USAGEMETER_BIN_DIR/UsageMeter" "$USAGEMETER_APP/Contents/MacOS/UsageMeter"
cp "$USAGEMETER_ROOT/Resources/Info.plist" "$USAGEMETER_APP/Contents/Info.plist"

if [[ -d "$USAGEMETER_ROOT/Resources/ProviderMarks" ]]; then
  mkdir -p "$USAGEMETER_APP/Contents/Resources/ProviderMarks"
  setopt local_options null_glob
  for USAGEMETER_MARK in "$USAGEMETER_ROOT"/Resources/ProviderMarks/*; do
    [[ "$USAGEMETER_MARK" == *.md ]] && continue
    cp "$USAGEMETER_MARK" "$USAGEMETER_APP/Contents/Resources/ProviderMarks/"
  done
fi

/usr/bin/swift "$USAGEMETER_ROOT/scripts/make-icon.swift" "$USAGEMETER_ICONSET" "$USAGEMETER_ROOT/Resources/icons/usagemeter.png"
/usr/bin/iconutil -c icns "$USAGEMETER_ICONSET" -o "$USAGEMETER_APP/Contents/Resources/AppIcon.icns"
/usr/bin/codesign --force --deep --sign - "$USAGEMETER_APP"

/usr/bin/plutil -lint "$USAGEMETER_APP/Contents/Info.plist"
/usr/bin/codesign --verify --deep --strict "$USAGEMETER_APP"
echo "Built $USAGEMETER_APP"
