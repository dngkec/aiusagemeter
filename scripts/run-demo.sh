#!/bin/zsh
set -euo pipefail

USAGEMETER_SCRIPT_DIR=${0:A:h}
USAGEMETER_ROOT=${USAGEMETER_SCRIPT_DIR:h}
USAGEMETER_APP="$USAGEMETER_ROOT/dist/UsageMeter.app"

if [[ ! -x "$USAGEMETER_APP/Contents/MacOS/UsageMeter" ]]; then
  "$USAGEMETER_ROOT/scripts/build-app.sh"
fi

export USAGEMETER_DEMO=1
export USAGEMETER_DEMO_EXPANDED=1
exec "$USAGEMETER_APP/Contents/MacOS/UsageMeter"
