#!/bin/bash
#
# Run the simulator without a window, e.g. on a server without a monitor.
#
# Unity batch mode opens no window and takes no input; the simulation then renders the sensors only, not the main view.
# Unity 2020.3 on Linux still needs an X server to set up the GPU, even in batch mode: xvfb-run provides a virtual
# one, while rendering stays on the GPU through Vulkan.
#
# Usage: run-headless.sh [path/to/simulator] [simulator arguments...]
#   run-headless.sh ./simulator
#   run-headless.sh ./simulator --apiport 8282 --data ~/svl-data-2    # a second instance
#

set -e

SIMULATOR=./simulator
if [ $# -gt 0 ] && [ -f "$1" ] && [ -x "$1" ]; then
    SIMULATOR=$1
    shift
fi

if ! command -v xvfb-run > /dev/null; then
    echo "xvfb-run not found, install the xvfb package" >&2
    exit 1
fi

exec xvfb-run -a -s "-screen 0 1280x720x24" "$SIMULATOR" -batchmode -force-vulkan "$@"
