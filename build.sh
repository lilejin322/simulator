#!/bin/bash
#
# Build the Linux simulator player from the command line, without opening the Unity editor.
#
# Unity 2020.3 on Linux needs an X server to set up the GPU, even in batch mode: xvfb-run provides a virtual one, and
# the shaders compile through Vulkan (-nographics crashes the shader compiler on the VFX compute shaders).
#
# Usage: build.sh [output folder] [--assets YAML] [--no-fetch | --fetch-only] [extra Unity arguments...]
#   build.sh                                    # into build/, using build-assets.yaml
#   build.sh build-small --assets build-assets-small.yaml
#   build.sh --fetch-only                       # fetch the missing enabled sources, do not build
#   build.sh ~/sim-debug -developmentBuild
# Edit build-assets.yaml: each exact source folder has a true/false switch, covering maps, vehicles, sensors, bridges,
# NPCs, pedestrians and controllables. Missing entries are disabled. --assets optionally selects another manifest.
#
# The editor is the project's version (ProjectSettings/ProjectVersion.txt), looked up in the install folders of the
# unity CLI and of Unity Hub (old and new); set UNITY to the editor executable to use one installed elsewhere. Either
# way the editor must report the project's version.
#
# Maps, vehicles, sensors, bridges, NPCs, pedestrians and controllables are built into the player from their sources in
# Assets/ (Assets/Maps/*/map.json and the like), which are not in the repository; enabled sources must be ready.
# Successful builds save the YAML and build-assets.resolved.json alongside the player.
#
# Every enabled source that is not installed is first fetched with curl from the Hugging Face dataset
# lilejin322/svl-content (set CONTENT_REPO for another one), as <category>/<folder>.tar.gz, together with the category's
# README. CONTENT_REVISION (default unity-2020) is resolved to one commit for the run; each file is checked against the
# hash Hugging Face reports for it, and HF_TOKEN is sent when set. An interrupted download continues on the next run.
# Installed sources are never replaced and disabled ones are not fetched: to update a source, delete its folder and run
# again. --no-fetch builds with the installed sources only; --fetch-only fetches and does not build.
#
# Missing OpenSSL 1.1 compatibility libraries for Unity 2020 are downloaded from Ubuntu into .build-tools/lib/.
# Existing libraries are reused; --no-fetch also disables this download. No system package installation is needed.
#
# The Unity log goes to Logs/build-linux64.log. If the output folder has no config.yml, one is created from
# config.yml.template; an existing config.yml is left as is.
#

set -e

ROOT=$(cd "$(dirname "$0")" && pwd)

OUTPUT=$ROOT/build
if [ $# -gt 0 ] && [[ $1 != -* ]]; then
    OUTPUT=$1
    shift
fi

ASSET_MANIFEST=$ROOT/build-assets.yaml
FETCH=true
FETCH_ONLY=false
UNITY_ARGS=()
while [ $# -gt 0 ]; do
    case $1 in
        --assets)
            if [ $# -lt 2 ] || [ -z "$2" ] || [[ $2 == -* ]]; then
                echo "--assets expects a YAML file path" >&2
                exit 1
            fi
            ASSET_MANIFEST=$2
            shift 2
            ;;
        --no-fetch)
            FETCH=false
            shift
            ;;
        --fetch-only)
            FETCH_ONLY=true
            shift
            ;;
        --maps|--vehicles|-builtinMaps|-builtinVehicles)
            echo "$1 was replaced by build-assets.yaml; set exact source folders to true or false" >&2
            exit 1
            ;;
        *)
            UNITY_ARGS+=("$1")
            shift
            ;;
    esac
done
if [ ! -f "$ASSET_MANIFEST" ]; then
    echo "Asset manifest not found: $ASSET_MANIFEST" >&2
    exit 1
fi
ASSET_MANIFEST=$(realpath "$ASSET_MANIFEST")
if ! $FETCH && $FETCH_ONLY; then
    echo "--no-fetch and --fetch-only exclude each other" >&2
    exit 1
fi

CONTENT_REPO=${CONTENT_REPO:-lilejin322/svl-content}
CONTENT_REVISION=${CONTENT_REVISION:-unity-2020}
CONTENT_URL=${HF_ENDPOINT:-https://huggingface.co}/datasets/$CONTENT_REPO/resolve
FETCH_JOBS=4

# The enabled sources of the manifest, one "<category folder> <source folder> <descriptor>" line each. This reads the
# block form build-assets.yaml is written in; Unity reads the manifest again and checks it fully.
enabled_sources() {
    awk '
        BEGIN {
            kind["maps"] = "Maps map.json"; kind["vehicles"] = "Vehicles vehicle.json"
            kind["sensors"] = "Sensors sensor.json"; kind["bridges"] = "Bridges bridge.json"
            kind["npcs"] = "NPCs npc.json"; kind["pedestrians"] = "Pedestrians pedestrian.json"
            kind["controllables"] = "Controllables controllable.json"
        }
        function fail(message) {
            printf "%s:%d: %s\n", FILENAME, FNR, message > "/dev/stderr"
            failed = 1
            exit 1
        }
        { sub(/\r$/, "") }
        /^[ \t]*(#.*)?$/ { next }
        /^version:/ { section = ""; next }
        /^[a-z]+:[ \t]*(\{[ \t]*\})?[ \t]*(#.*)?$/ {
            section = substr($0, 1, index($0, ":") - 1)
            if (!(section in kind)) fail("unknown section " section)
            next
        }
        /^[ \t]/ {
            line = $0
            sub(/^[ \t]+/, "", line)
            if (section == "") fail("entry outside a section")
            if (match(line, /^"[A-Za-z0-9_.+-]+"/)) name = substr(line, 2, RLENGTH - 2)
            else if (match(line, /^[A-Za-z0-9_][A-Za-z0-9_.+-]*/)) name = substr(line, 1, RLENGTH)
            else fail("expected a source folder name, plain or in double quotes")
            value = substr(line, RLENGTH + 1)
            if (!sub(/^[ \t]*:[ \t]+/, "", value)) fail("expected \"" name "\": true or false")
            sub(/[ \t]*(#.*)?$/, "", value)
            if (value == "true") { split(kind[section], k, " "); print k[1], name, k[2] }
            else if (value != "false") fail("expected \"" name "\": true or false")
            next
        }
        { fail("cannot read this line; build.sh reads the block form of build-assets.yaml") }
        END { if (failed) exit 1 }
    ' "$ASSET_MANIFEST"
}

# curl with the Hugging Face token, if any, passed in a header file rather than on the command line
hf_curl() {
    if [ -n "${HF_TOKEN:-}" ]; then
        curl -H @<(printf 'Authorization: Bearer %s\n' "$HF_TOKEN") "$@"
    else
        curl "$@"
    fi
}

# a size in bytes as MB, or KB below 1 MB
size_text() {
    if [ "$1" -ge 1000000 ]; then
        echo "$(($1 / 1000000)) MB"
    else
        echo "$((($1 + 999) / 1000)) KB"
    fi
}

# hash of a file as Hugging Face reports it: sha256 for LFS files, the git blob sha1 for others
file_hash() {
    if [ ${#2} -eq 64 ]; then
        sha256sum < "$1" | cut -d " " -f 1
    else
        { printf 'blob %d\0' "$(stat -c %s "$1")"; cat "$1"; } | sha1sum | cut -d " " -f 1
    fi
}

# fetch_one <path in the content repository> <hash> <size>: download, check and install one file
fetch_one() {
    local FILE=$1 HASH=$2 SIZE=$3
    local KIND=${FILE%%/*} NAME=${FILE#*/}
    local DIR=$ROOT/Assets/$KIND
    # names starting with a dot are ignored by Unity, and Assets/<category>/ by git
    local PART=$DIR/.$NAME.partial
    mkdir -p "$DIR"
    # only a file of known size is continued; a longer one cannot be
    if [ -f "$PART" ] && { [ "$SIZE" -eq 0 ] || [ "$(stat -c %s "$PART")" -gt "$SIZE" ]; }; then
        rm -f "$PART"
    fi
    if ! [ -f "$PART" ] || [ "$SIZE" -eq 0 ] || [ "$(stat -c %s "$PART")" -lt "$SIZE" ]; then
        if ! hf_curl -fsSL --retry 5 --retry-delay 3 -C - -o "$PART" "$CONTENT_URL/$COMMIT/$FILE"; then
            echo "Downloading $FILE failed; run build.sh again to continue" >&2
            return 1
        fi
    fi
    if [ "$(file_hash "$PART" "$HASH")" != "$HASH" ]; then
        rm -f "$PART"
        echo "$FILE does not match its hash and was deleted; run build.sh again to download it again" >&2
        return 1
    fi
    if [[ $NAME != *.tar.gz ]]; then
        mv -f "$PART" "$DIR/$NAME"
        return 0
    fi
    # a package holds Assets/<category>/<folder>/ and <folder>.meta: unpack it aside, check it, then move it in place,
    # the folder last, so that an interrupted run leaves no partly installed source; files get the current time
    local FOLDER=${NAME%.tar.gz}
    local STAGE=$DIR/.$FOLDER.unpack
    rm -rf "$STAGE"
    mkdir "$STAGE"
    if ! tar -xzmf "$PART" -C "$STAGE"; then
        rm -rf "$STAGE"
        echo "Unpacking $FILE failed" >&2
        return 1
    fi
    if [ "$(ls -A "$STAGE")" != Assets ] || [ "$(ls -A "$STAGE/Assets")" != "$KIND" ] ||
            [ "$(ls -A "$STAGE/Assets/$KIND" | LC_ALL=C sort | tr '\n' ' ')" != "$FOLDER $FOLDER.meta " ] ||
            [ -n "$(find "$STAGE" ! -type f ! -type d -print -quit)" ]; then
        rm -rf "$STAGE"
        echo "$FILE does not hold just Assets/$KIND/$FOLDER and its .meta" >&2
        return 1
    fi
    if [ -e "$DIR/$FOLDER" ]; then
        rm -rf "$STAGE"
        echo "Assets/$KIND/$FOLDER appeared while it was fetched; it is left as is" >&2
        return 1
    fi
    mv -f "$STAGE/Assets/$KIND/$FOLDER.meta" "$DIR/$FOLDER.meta"
    mv "$STAGE/Assets/$KIND/$FOLDER" "$DIR/$FOLDER"
    rm -rf "$STAGE" "$PART"
    echo "Fetched Assets/$KIND/$FOLDER ($(size_text "$SIZE"))"
}

# Fetch the enabled sources that are not installed, and the README files of their categories.
fetch_assets() {
    local SOURCES KIND FOLDER DESCRIPTOR NAME
    local WANTED=() OPTIONAL=()
    SOURCES=$(enabled_sources)
    # two runs fetching into one project would write the same files
    exec 9< "$ROOT/Assets"
    if command -v flock > /dev/null && ! flock -n 9; then
        echo "Another build.sh is fetching sources into $ROOT/Assets" >&2
        exit 1
    fi
    # left behind by an interrupted run
    rm -rf "$ROOT"/Assets/*/.*.unpack
    while read -r KIND FOLDER DESCRIPTOR; do
        [ -n "$KIND" ] || continue
        if [ -f "$ROOT/Assets/$KIND/$FOLDER/$DESCRIPTOR" ]; then
            continue
        fi
        if [ -e "$ROOT/Assets/$KIND/$FOLDER" ]; then
            echo "Assets/$KIND/$FOLDER has no $DESCRIPTOR; move it away to fetch the source again" >&2
            exit 1
        fi
        WANTED+=("$KIND/$FOLDER.tar.gz")
        for NAME in README.md README.md.meta; do
            if [ ! -e "$ROOT/Assets/$KIND/$NAME" ] && [[ " ${OPTIONAL[*]} " != *" $KIND/$NAME "* ]]; then
                OPTIONAL+=("$KIND/$NAME")
            fi
        done
    done <<< "$SOURCES"
    if [ ${#WANTED[@]} -eq 0 ]; then
        exec 9<&-
        return 0
    fi
    if ! command -v curl > /dev/null; then
        echo "${#WANTED[@]} enabled sources are not installed, and fetching them needs curl" >&2
        exit 1
    fi

    # one commit for the whole run, so that every file comes from the same version of the content
    local HEADERS
    if ! HEADERS=$(hf_curl -sSI --retry 3 "$CONTENT_URL/$CONTENT_REVISION/.gitattributes"); then
        echo "Cannot reach ${HF_ENDPOINT:-https://huggingface.co} to fetch ${#WANTED[@]} enabled sources" >&2
        exit 1
    fi
    HEADERS=${HEADERS//$'\r'/}
    COMMIT=$(awk 'tolower($1) == "x-repo-commit:" { print $2 }' <<< "$HEADERS")
    if [[ ! $COMMIT =~ ^[0-9a-f]{40}$ ]]; then
        echo "Revision $CONTENT_REVISION of $CONTENT_REPO not found: $(head -1 <<< "$HEADERS")" >&2
        exit 1
    fi

    # the hash and size of every file, asked all at once, before anything is downloaded
    local TMP FILES=("${WANTED[@]}" "${OPTIONAL[@]}") I
    TMP=$(mktemp -d)
    for I in "${!FILES[@]}"; do
        printf 'url = "%s"\noutput = "%s"\n' "$CONTENT_URL/$COMMIT/${FILES[$I]}" "$TMP/$I"
    done > "$TMP/config"
    if ! hf_curl -sSI --retry 3 --parallel --parallel-max 8 --config "$TMP/config"; then
        rm -rf "$TMP"
        echo "Cannot reach ${HF_ENDPOINT:-https://huggingface.co} to fetch ${#WANTED[@]} enabled sources" >&2
        exit 1
    fi
    local LIST=() MISSING=() TOTAL=0 STATUS HASH SIZE
    for I in "${!FILES[@]}"; do
        STATUS=$(awk 'NR == 1 { print $2 }' "$TMP/$I")
        HASH=$(tr -d '\r"' < "$TMP/$I" | awk 'tolower($1) == "x-linked-etag:" { print $2 }')
        SIZE=$(tr -d '\r' < "$TMP/$I" | awk 'tolower($1) == "x-linked-size:" { print $2 }')
        if [ "$STATUS" = 404 ] && [ "$I" -ge ${#WANTED[@]} ]; then
            continue
        elif [ "$STATUS" = 404 ]; then
            MISSING+=("${FILES[$I]}")
        elif [[ ! $HASH =~ ^([0-9a-f]{40}|[0-9a-f]{64})$ ]]; then
            echo "Unexpected answer for ${FILES[$I]}: $(head -1 "$TMP/$I" | tr -d '\r')" >&2
            rm -rf "$TMP"
            exit 1
        else
            LIST+=("${SIZE:-0} ${FILES[$I]} $HASH")
            TOTAL=$((TOTAL + ${SIZE:-0}))
        fi
    done
    rm -rf "$TMP"
    if [ ${#MISSING[@]} -gt 0 ]; then
        echo "Not in $CONTENT_REPO at ${COMMIT:0:7}, check the folder names in $ASSET_MANIFEST:" >&2
        printf '  %s\n' "${MISSING[@]}" >&2
        exit 1
    fi

    echo "Fetching ${#WANTED[@]} sources ($(size_text "$TOTAL")) from $CONTENT_REPO at ${COMMIT:0:7}"
    # largest first, a few at a time; xargs runs in the foreground, so Ctrl-C stops the downloads too (commands put in
    # the background by a script ignore it)
    export -f fetch_one hf_curl file_hash size_text
    export ROOT CONTENT_URL COMMIT
    if ! printf '%s\n' "${LIST[@]}" | sort -k 1,1nr |
            xargs -P $FETCH_JOBS -L 1 bash -ec 'fetch_one "$2" "$3" "$1"' fetch_one; then
        echo "Some files could not be fetched, see above" >&2
        exit 1
    fi
    exec 9<&-
}

if $FETCH_ONLY; then
    fetch_assets
    echo "All sources enabled in $ASSET_MANIFEST are installed"
    exit 0
fi

# Only unpack the compatibility libraries; do not install an old OpenSSL package into the system.
ensure_openssl() (
    LIB_DIR=$ROOT/.build-tools/lib
    if [ -s "$LIB_DIR/libssl.so.1.1" ] && [ -s "$LIB_DIR/libcrypto.so.1.1" ]; then
        return 0
    fi
    if ! $FETCH; then
        echo "OpenSSL 1.1 compatibility libraries are missing in $LIB_DIR; run build.sh without --no-fetch to download them" >&2
        exit 1
    fi
    if [ "$(uname -s)" != Linux ] || [ "$(uname -m)" != x86_64 ]; then
        echo "Automatic OpenSSL 1.1 setup for Unity 2020 requires Linux x86_64" >&2
        exit 1
    fi
    for TOOL in curl dpkg-deb sha256sum; do
        if ! command -v "$TOOL" > /dev/null; then
            echo "Downloading OpenSSL 1.1 compatibility libraries needs $TOOL" >&2
            exit 1
        fi
    done

    mkdir -p "$LIB_DIR"
    STAGE=$(mktemp -d "$ROOT/.build-tools/.openssl.XXXXXX")
    trap 'rm -rf "$STAGE"' EXIT
    PACKAGE=libssl1.1_1.1.1f-1ubuntu2.24_amd64.deb
    HASH=7cf39d70a639017d1dd7c8d36daa2258063608688e449fddf40ffdd46f992a78
    echo "Downloading OpenSSL 1.1 compatibility libraries into $LIB_DIR"
    if ! curl -fsSL --connect-timeout 15 --max-time 120 --retry 3 -o "$STAGE/$PACKAGE" \
            "https://security.ubuntu.com/ubuntu/pool/main/o/openssl/$PACKAGE"; then
        echo "Downloading OpenSSL 1.1 failed; run build.sh again to retry" >&2
        exit 1
    fi
    if ! printf '%s  %s\n' "$HASH" "$STAGE/$PACKAGE" | sha256sum --check --status; then
        echo "OpenSSL 1.1 package failed SHA-256 verification; run build.sh again to retry" >&2
        exit 1
    fi
    if ! dpkg-deb -x "$STAGE/$PACKAGE" "$STAGE/unpack"; then
        echo "Unpacking OpenSSL 1.1 failed" >&2
        exit 1
    fi
    UNPACKED=$STAGE/unpack/usr/lib/x86_64-linux-gnu
    if [ ! -s "$UNPACKED/libssl.so.1.1" ] || [ ! -s "$UNPACKED/libcrypto.so.1.1" ]; then
        echo "OpenSSL 1.1 package is missing the compatibility libraries" >&2
        exit 1
    fi
    mv -f "$UNPACKED/libcrypto.so.1.1" "$LIB_DIR/libcrypto.so.1.1"
    mv -f "$UNPACKED/libssl.so.1.1" "$LIB_DIR/libssl.so.1.1"
)

# Unity 2020's Roslyn uses an older .NET runtime than modern Linux distributions provide.
ensure_openssl
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="${DOTNET_SYSTEM_GLOBALIZATION_INVARIANT:-1}"
export LD_LIBRARY_PATH="$ROOT/.build-tools/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

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

LOG=$ROOT/Logs/build-linux64.log
SETTINGS=ProjectSettings/ProjectSettings.asset

if ! command -v xvfb-run > /dev/null; then
    echo "xvfb-run not found, install the xvfb package" >&2
    exit 1
fi

if $FETCH; then
    fetch_assets
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

echo "Building into $OUTPUT with Unity $VERSION, assets: $ASSET_MANIFEST, log: $LOG"

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
    -assetManifest "$ASSET_MANIFEST" \
    -logFile "$LOG" \
    "${UNITY_ARGS[@]}"
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
    cp "$ROOT/config.yml.template" "$OUTPUT/config.yml"
    echo "Created $OUTPUT/config.yml"
fi

echo "Build succeeded: $OUTPUT/simulator"
