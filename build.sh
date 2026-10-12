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
# The local asset library is LocalAssets in this repo: when it is missing or incomplete, it is first downloaded from
# the Hugging Face dataset lilejin322/svl-assets with the hf CLI (about 12 GB; run again to continue an interrupted
# download, files already downloaded are kept).
#
# The Unity log goes to Logs/build-linux64.log. If the output folder has no config.yml, one is created pointing
# assets_path at this repo's LocalAssets; an existing config.yml is left as is.
#

set -e

ROOT=$(cd "$(dirname "$0")" && pwd)

ASSETS=$ROOT/LocalAssets
ASSETS_REPO=lilejin322/svl-assets

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

# complete: every file of the dataset is there with its size, by the file list hf saves with a download
# (.cache/huggingface/trees/<commit>.json); without that list the library has not been checked against the dataset yet
assets_complete() {
    python3 -I - "$ASSETS" << 'EOF'
import glob, json, os, sys
root = sys.argv[1]
trees = sorted(glob.glob(os.path.join(root, ".cache", "huggingface", "trees", "*.json")), key=os.path.getmtime)
if not trees:
    sys.exit(1)
with open(trees[-1]) as f:
    files = {path: info for path, info in json.load(f)["files"].items() if path != ".gitattributes"}
missing = [path for path, info in files.items()
           if not (os.path.isfile(os.path.join(root, path)) and os.path.getsize(os.path.join(root, path)) == info["size"])]
if missing:
    print(f"{len(missing)} of {len(files)} files of the asset library are missing or incomplete", file=sys.stderr)
    sys.exit(1)
EOF
}

if ! command -v python3 > /dev/null; then
    echo "python3 not found, it is needed to check the asset library" >&2
    exit 1
fi

if ! assets_complete; then
    if ! command -v hf > /dev/null; then
        echo "The asset library $ASSETS is missing or incomplete. Downloading it needs the Hugging Face CLI:" >&2
        echo "  curl -LsSf https://hf.co/cli/install.sh | bash -s" >&2
        exit 1
    fi
    echo "Downloading the asset library (about 12 GB) from https://huggingface.co/datasets/$ASSETS_REPO into $ASSETS"
    if ! hf download "$ASSETS_REPO" --repo-type dataset --local-dir "$ASSETS" --exclude .gitattributes; then
        echo "Downloading the asset library failed; run build.sh again to continue, files already downloaded are kept" >&2
        exit 1
    fi
    # an interrupted download leaves its partial files behind, and hf downloads those files again from the start
    if [ -d "$ASSETS/.cache/huggingface/download" ]; then
        find "$ASSETS/.cache/huggingface/download" -name "*.incomplete" -delete
    fi
    # an hf version that saves no file list leaves nothing to check; its successful download has to do
    if ls "$ASSETS"/.cache/huggingface/trees/*.json > /dev/null 2>&1 && ! assets_complete; then
        echo "The asset library $ASSETS is still incomplete after the download" >&2
        exit 1
    fi
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
    sed "s|^assets_path:.*|assets_path: \"$ASSETS\"|" "$ROOT/config.yml.template" > "$OUTPUT/config.yml"
    echo "Created $OUTPUT/config.yml"
fi

echo "Build succeeded: $OUTPUT/simulator"
