#!/bin/zsh
set -euo pipefail

AIUSAGEMETER_SCRIPT_DIR=${0:A:h}
AIUSAGEMETER_ROOT=${AIUSAGEMETER_SCRIPT_DIR:h}
AIUSAGEMETER_SCRATCH="$AIUSAGEMETER_ROOT/.build/package"
AIUSAGEMETER_APP="$AIUSAGEMETER_ROOT/dist/AIUsageMeter.app"
AIUSAGEMETER_ICONSET="$AIUSAGEMETER_ROOT/.build/AppIcon.iconset"

export CLANG_MODULE_CACHE_PATH="$AIUSAGEMETER_ROOT/.build/clang-cache"
export SWIFTPM_MODULECACHE_OVERRIDE="$AIUSAGEMETER_ROOT/.build/swiftpm-cache"

mkdir -p "$AIUSAGEMETER_ROOT/.build" "$AIUSAGEMETER_ROOT/dist"
AIUSAGEMETER_BIN_DIR=$(swift build --package-path "$AIUSAGEMETER_ROOT" --scratch-path "$AIUSAGEMETER_SCRATCH" -c release --show-bin-path)
swift build --package-path "$AIUSAGEMETER_ROOT" --scratch-path "$AIUSAGEMETER_SCRATCH" -c release --product AIUsageMeter -Xswiftc -warnings-as-errors

rm -rf "$AIUSAGEMETER_APP" "$AIUSAGEMETER_ICONSET"
mkdir -p "$AIUSAGEMETER_APP/Contents/MacOS" "$AIUSAGEMETER_APP/Contents/Resources"
cp "$AIUSAGEMETER_BIN_DIR/AIUsageMeter" "$AIUSAGEMETER_APP/Contents/MacOS/AIUsageMeter"
cp "$AIUSAGEMETER_ROOT/Resources/Info.plist" "$AIUSAGEMETER_APP/Contents/Info.plist"

if [[ -d "$AIUSAGEMETER_ROOT/Resources/ProviderMarks" ]]; then
  mkdir -p "$AIUSAGEMETER_APP/Contents/Resources/ProviderMarks"
  setopt local_options null_glob
  for AIUSAGEMETER_MARK in "$AIUSAGEMETER_ROOT"/Resources/ProviderMarks/*; do
    [[ "$AIUSAGEMETER_MARK" == *.md ]] && continue
    cp "$AIUSAGEMETER_MARK" "$AIUSAGEMETER_APP/Contents/Resources/ProviderMarks/"
  done
fi

/usr/bin/swift "$AIUSAGEMETER_ROOT/scripts/make-icon.swift" "$AIUSAGEMETER_ICONSET" "$AIUSAGEMETER_ROOT/Resources/icons/aiusagemeter.png"
/usr/bin/iconutil -c icns "$AIUSAGEMETER_ICONSET" -o "$AIUSAGEMETER_APP/Contents/Resources/AppIcon.icns"
/usr/bin/codesign --force --deep --sign - "$AIUSAGEMETER_APP"

/usr/bin/plutil -lint "$AIUSAGEMETER_APP/Contents/Info.plist"
/usr/bin/codesign --verify --deep --strict "$AIUSAGEMETER_APP"
echo "Built $AIUSAGEMETER_APP"
