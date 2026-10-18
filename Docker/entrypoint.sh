#!/bin/sh
# Starts the simulator on a private virtual X screen (xvfb-run -a); rendering stays on the GPU through Vulkan.
# Runs as any uid:gid: HOME and temp files live in writable places, data goes to /data.
set -e
export HOME="${HOME:-/tmp/home}"
[ "$HOME" = "/" ] && export HOME=/tmp/home
mkdir -p "$HOME" 2>/dev/null || true
if [ ! -w /data ]; then
    echo "WARNING: /data is not writable by uid $(id -u); mount a host folder owned by this user" >&2
fi
exec xvfb-run -a -s "-screen 0 1280x720x24" /opt/simulator/simulator \
    -batchmode -force-vulkan -logFile - --data /data "$@"
