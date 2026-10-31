#!/bin/bash
#
# Run the simulator without a window, e.g. on a server without a monitor.
#
# Unity batch mode opens no window and takes no input; the simulation then renders the sensors only, not the main view.
# Unity 6 on Linux needs no X server in batch mode: rendering runs on the GPU through Vulkan without a display.
#
# With more than one GPU, Unity's documented player argument -force-device-index N selects which one to use. The
# player is started with -force-vulkan, so N is the Vulkan device index (the GPU0, GPU1, ... order from vulkaninfo).
# That index can differ from the one printed by nvidia-smi, coz integrated GPU may also be listed in vulkan info.
# TODO: check if the index is correct, should have a machine with 1*integrated + nGPUs to test
# The player is build/simulator from build.sh, next to this script. Pass another executable to use that instead.
# Usage: run-headless.sh [path/to/simulator] [simulator arguments...]
#   run-headless.sh
#   run-headless.sh --apiport 8282 --data ~/svl-data-2       # a second instance, port & data dir should be different
#   run-headless.sh -force-device-index 1                    # the second Vulkan device
#

set -e

ROOT=$(cd "$(dirname "$0")" && pwd)

SIMULATOR=$ROOT/build/simulator
if [ $# -gt 0 ] && [ -f "$1" ] && [ -x "$1" ]; then
    SIMULATOR=$1
    shift
fi

if [ ! -x "$SIMULATOR" ]; then
    echo "simulator not found at $SIMULATOR, build it with build.sh or pass the player path" >&2
    exit 1
fi

exec "$SIMULATOR" -batchmode -force-vulkan "$@"
