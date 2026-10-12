#!/bin/bash
#
# Build the Linux simulator player from the command line, without opening the Unity editor.
#
# Unity 2020.3 on Linux needs an X server to set up the GPU, even in batch mode: xvfb-run provides a virtual one, and
# the shaders compile through Vulkan (-nographics crashes the shader compiler on the VFX compute shaders).
#
# Usage: build.sh [output folder] [extra Unity arguments...]
#   build.sh                                    # into build/ in this repo
#   build.sh ~/sim-debug -developmentBuild
#
# The editor is the project's version (ProjectSettings/ProjectVersion.txt), looked up in the install folders of the
# unity CLI and of Unity Hub (old and new); set UNITY to the editor executable to use one installed elsewhere. Either
# way the editor must report the project's version.
#
# The Unity log goes to Logs/build-linux64.log. If the output folder has no config.yml, one is created pointing
# assets_path at this repo's LocalAssets; an existing config.yml is left as is.
#

set -e

ROOT=$(cd "$(dirname "$0")" && pwd)

VERSION=$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt")

# the editor prints its version and exits
editor_version() {
    "$1" -version 2> /dev/null | tail -1
}

if [ -n "${UNITY:-}" ]; then
    if [ ! -x "$UNITY" ]; then
        echo "UNITY=$UNITY is not an executable" >&2
        exit 1
    fi
    UNITY_VERSION=$(editor_version "$UNITY")
    if [ "$UNITY_VERSION" != "$VERSION" ]; then
        echo "UNITY=$UNITY is Unity ${UNITY_VERSION:-of unknown version}, the project needs $VERSION" >&2
        exit 1
    fi
else
    EDITORS=()
    if command -v unity > /dev/null; then
        EDITORS+=("$(unity --no-banner --non-interactive install-path -g 2> /dev/null)")
    fi
    # an install folder changed in Unity Hub's settings; the settings folder is UnityHub for Hub 2 and 3 but unityhub for
    # the unity CLI, so it is matched ignoring case
    while IFS= read -r FILE; do
        EDITORS+=("$(tr -d '"' < "$FILE")")
    done < <(find "$HOME/.config" -mindepth 2 -maxdepth 2 -ipath "*/unityhub/secondaryinstallpath.json" 2> /dev/null)
    EDITORS+=("$HOME/Unity/Hub/Editor")
    for DIR in "${EDITORS[@]}"; do
        # editors are installed as <version>/Editor/Unity; Unity's manual gives <version>/Unity for Hub on Linux
        for EDITOR in "$DIR/$VERSION/Editor/Unity" "$DIR/$VERSION/Unity"; do
            if [ -n "$DIR" ] && [ -f "$EDITOR" ] && [ -x "$EDITOR" ] && [ "$(editor_version "$EDITOR")" = "$VERSION" ]; then
                UNITY=$EDITOR
                break 2
            fi
        done
    done
    if [ -z "${UNITY:-}" ]; then
        echo "Unity $VERSION not found: install it with Unity Hub, or set UNITY to its editor executable" >&2
        exit 1
    fi
fi

OUTPUT=$ROOT/build
if [ $# -gt 0 ] && [[ $1 != -* ]]; then
    OUTPUT=$1
    shift
fi

LOG=$ROOT/Logs/build-linux64.log
SETTINGS=ProjectSettings/ProjectSettings.asset

if ! command -v xvfb-run > /dev/null; then
    echo "xvfb-run not found, install the xvfb package" >&2
    exit 1
fi

mkdir -p "$OUTPUT" "$ROOT/Logs"
OUTPUT=$(cd "$OUTPUT" && pwd)

# the build overwrites the player files, which crashes a simulator running from them
for PID in $(pgrep -x simulator); do
    if [ "$(readlink /proc/$PID/exe)" = "$OUTPUT/simulator" ]; then
        echo "simulator (pid $PID) is running from $OUTPUT, close it first" >&2
        exit 1
    fi
done

# the build saves player settings into ProjectSettings.asset; restore it afterwards unless it already had local changes
SETTINGS_CLEAN=false
if git -C "$ROOT" diff --quiet -- $SETTINGS 2> /dev/null; then
    SETTINGS_CLEAN=true
fi

echo "Building into $OUTPUT with Unity $VERSION, log: $LOG"

# Simulator.Editor.Build.Run logs its errors and exits with 0, so success is read from a fresh log
rm -f "$LOG"
set +e
xvfb-run -a -s "-screen 0 1920x1080x24" "$UNITY" \
    -batchmode \
    -force-vulkan \
    -silent-crashes \
    -projectPath "$ROOT" \
    -executeMethod Simulator.Editor.Build.Run \
    -buildTarget Linux64 \
    -buildPlayer "$OUTPUT" \
    -logFile "$LOG" \
    "$@"
RESULT=$?
set -e

if $SETTINGS_CLEAN; then
    git -C "$ROOT" checkout -- $SETTINGS
fi

if [ $RESULT -ne 0 ] || ! grep -q "^Player build succeeded!" "$LOG" 2> /dev/null ||
        ! bash "$ROOT/Jenkins/check-unity-log.sh" "$LOG" > /dev/null; then
    grep -E "error CS|^[A-Za-z.]*Exception: |threw exception|Fatal Error|Caught fatal signal|Player build result" "$LOG" 2> /dev/null |
        sort -u | head -20 >&2
    echo "Build failed (Unity exit code $RESULT), see $LOG" >&2
    exit 1
fi

# make sure the player and the ffmpeg it records video with can be run
chmod +x "$OUTPUT/simulator" "$OUTPUT/simulator_Data/Plugins/ffmpeg"

if [ ! -f "$OUTPUT/config.yml" ]; then
    sed "s|^assets_path:.*|assets_path: \"$ROOT/LocalAssets\"|" "$ROOT/config.yml.template" > "$OUTPUT/config.yml"
    echo "Created $OUTPUT/config.yml"
fi

echo "Build succeeded: $OUTPUT/simulator"
