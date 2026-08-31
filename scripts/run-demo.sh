#!/bin/zsh
set -euo pipefail

AIUSAGEMETER_SCRIPT_DIR=${0:A:h}
AIUSAGEMETER_ROOT=${AIUSAGEMETER_SCRIPT_DIR:h}
AIUSAGEMETER_APP="$AIUSAGEMETER_ROOT/dist/AIUsageMeter.app"

if [[ ! -x "$AIUSAGEMETER_APP/Contents/MacOS/AIUsageMeter" ]]; then
  "$AIUSAGEMETER_ROOT/scripts/build-app.sh"
fi

export AIUSAGEMETER_DEMO=1
export AIUSAGEMETER_DEMO_EXPANDED=1
exec "$AIUSAGEMETER_APP/Contents/MacOS/AIUsageMeter"
