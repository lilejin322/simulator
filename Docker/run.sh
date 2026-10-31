#!/bin/sh
#
# Start one simulator container in the background, no display needed.
# Usage: Docker/run.sh [--name NAME] HOST_PORT GPU ASSETS_DIR DATA_DIR [simulator arguments...]
#   Docker/run.sh 8181 0 /path/to/LocalAssets ~/svl-data-1
#   Docker/run.sh --name sim-b 8182 0 /path/to/LocalAssets ~/svl-data-2   # second instance
# --name     container name (optional, default simulator-HOST_PORT, so instances never collide)
# HOST_PORT  API port; the container listens on the same port (-p PORT:PORT --apiport PORT),
#            because the API server (websocket-sharp) rejects clients whose Host port differs
# GPU        GPU index as in nvidia-smi (docker --gpus device=N); the container only sees that GPU
# ASSETS_DIR local asset library, mounted read-only at /assets
# DATA_DIR   cache/logs/videos, mounted at /data; created if missing, files belong to the calling user
# Host services (ROS/Apollo bridges): use host.docker.internal, not localhost (that is the container).
# Image: $SIM_IMAGE (default svlsimulator).
#
set -e

usage() { sed -n '3,15p' "$0" | sed 's/^# \{0,1\}//'; exit 1; }

NAME=
if [ "$1" = "--name" ] || [ "$1" = "-n" ]; then
    [ -n "$2" ] || usage
    NAME=$2
    shift 2
fi
[ $# -ge 4 ] || usage

PORT=$1; GPU=$2; ASSETS=$3; DATA=$4
shift 4
NAME=${NAME:-simulator-$PORT}
IMAGE=${SIM_IMAGE:-svlsimulator}

[ -d "$ASSETS" ] || { echo "assets folder not found: $ASSETS" >&2; exit 1; }
mkdir -p "$DATA"
ASSETS=$(cd "$ASSETS" && pwd)
DATA=$(cd "$DATA" && pwd)

docker run -d \
    --name "$NAME" \
    --gpus "device=$GPU" \
    --user "$(id -u):$(id -g)" \
    -p "$PORT:$PORT" \
    --add-host=host.docker.internal:host-gateway \
    -v "$ASSETS:/assets:ro" \
    -v "$DATA:/data" \
    "$IMAGE" --apiport "$PORT" "$@"

echo "started $NAME: API on port $PORT, logs: docker logs -f $NAME"
