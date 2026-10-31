# Running SVL Simulator in Docker

The `assets` branch's runtime image contains the Linux player and Xvfb. The prebuilt SVL/WISE bundle library is
mounted from the host at `/assets`; it is not included in the image. A separate `/data` mount stores runtime
data. Rendering uses the GPU through Vulkan, with Xvfb providing a virtual display inside each container.

## Requirements

- Docker Engine and an NVIDIA GPU with a working host driver.
- NVIDIA Container Toolkit, with GPU access through `docker run --gpus`.
- A readable bundle library from [lilejin322/svl-assets](https://huggingface.co/datasets/lilejin322/svl-assets).
- A writable data directory for each simulator instance.

Unity is needed to build the player, but is not required on container hosts. The host does not need Xvfb, an X
server or a display socket; those are handled inside the image.

## Build and distribute the image

From the repository root, follow the [build requirements](../README.md#requirements), then run:

```bash
./build.sh
docker build -f Docker/Dockerfile -t svlsimulator .
```

The default Dockerfile expects the player in `build/`. The root `.dockerignore` limits the build context to
`build/` and `Docker/`, excluding debug symbols (`build/*.debug`) and `build/config.yml`. The image uses
[Docker/config.yml](config.yml) as its runtime configuration.

Distribute the image through a container registry or with `docker save` and `docker load`. The image and the
asset library are deployed separately. On a container host without the library, download it with the `hf` CLI:

```bash
hf download lilejin322/svl-assets --repo-type dataset --local-dir /path/to/LocalAssets --exclude .gitattributes
```

See the [asset-library layout](../README.md#local-asset-library) for bundle and metadata directories.

## Start a container

```text
Docker/run.sh [--name NAME] HOST_PORT GPU ASSETS_DIR DATA_DIR [simulator arguments...]
```

From the repository root, using the library downloaded by `build.sh`:

```bash
Docker/run.sh 8181 0 "$PWD/LocalAssets" "$HOME/svl-data-1"
```

| Parameter | Meaning |
|-----------|---------|
| `--name NAME` | Optional container name; defaults to `simulator-HOST_PORT`. |
| `HOST_PORT` | Python API port, used on both the host and the container. |
| `GPU` | NVIDIA GPU index passed to `--gpus device=N`; only that GPU is exposed to the container. |
| `ASSETS_DIR` | Existing host bundle library, mounted read-only at `/assets`. |
| `DATA_DIR` | Runtime data directory, mounted read-write at `/data` and created if missing. |

`SIM_IMAGE` selects a different image name or tag; its default is `svlsimulator`. Additional arguments after
`DATA_DIR` are passed to the simulator, for example `--no-api-only`.

The wrapper runs the container with the calling user's UID and GID. That user must be able to read the asset
library and write to the data directory. Files created in `/data` remain owned by the same user on the host.

Python API clients connect to the host's address and chosen port:

```python
import lgsvl

sim = lgsvl.Simulator("HOST", 8181)
```

Inspect logs and remove the example container with:

```bash
docker logs -f simulator-8181
docker rm -f simulator-8181
```

Removing the container leaves both host directories intact.

## Files and configuration

| Container path | Source and purpose |
|----------------|--------------------|
| `/opt/simulator/` | Linux player copied from `build/` when the image is built. |
| `/opt/simulator/config.yml` | Configuration copied from `Docker/config.yml`. |
| `/usr/local/bin/simulator-entrypoint` | Startup script, run under `tini`. |
| `/assets` | Read-only host library: bundles, WISE metadata and supplemental HD map files. |
| `/data` | Writable host directory: database, cache links, logs, saved sensor output and videos. |
| `/tmp/home` | Writable home directory for Unity settings; removed with the container. |

The player links bundles from `/assets` into its cache under `/data`. Keep the asset mount available for the
container's lifetime. Multiple containers can share one asset library, but each must have its own data directory:
instances lock their database and cannot safely share `/data`.

The image configuration sets:

| Key | Value | Purpose |
|-----|-------|---------|
| `api_hostname` | `"*"` | Accept connections through the published API port. |
| `api_port` | `8181` | Default; `run.sh` overrides it with `--apiport HOST_PORT`. |
| `assets_path` | `/assets` | Mounted bundle library. |
| `data_path` | `/data` | Persistent runtime data. |
| `headless` | `true` | Render sensors without a main view. |
| `start_api_only` | `true` | Start an API Only simulation on launch. |

The entrypoint runs the player with `-batchmode -force-vulkan` through `xvfb-run -a`. Each container gets its
own virtual display. `-logFile -` sends the Unity log to `docker logs`. The simulator may log
`Unknown argument -force-vulkan, skipping it` from its own parser; Unity still receives this rendering option.

## Ports and multiple instances

Host and container API port numbers must match. The API server rejects requests whose `Host` port differs from
its listening port, so use `-p 8182:8182 --apiport 8182` rather than `-p 8182:8181`. The wrapper sets both values.

Each instance needs a distinct host port, container name and data directory. Instances can share the library
and can use the same GPU when capacity allows:

```bash
Docker/run.sh 8181 0 "$PWD/LocalAssets" "$HOME/svl-data-1"
Docker/run.sh --name sim-b 8182 0 "$PWD/LocalAssets" "$HOME/svl-data-2"
```

Only the Python API port is published by these examples. UDP port 9999, used by cluster simulations, is not
published by `run.sh`.

## Connecting bridges to the host

Inside a container, `localhost` refers to that container. `run.sh` adds
`--add-host=host.docker.internal:host-gateway`, so bridges can reach host services through
`host.docker.internal`, for example `host.docker.internal:9090`.

Use this hostname or the host's IP in ROS, Apollo or Autoware bridge connection strings. These examples use
Docker bridge networking with explicit port publishing.

## Running without the wrapper

From the repository root, the first example can also be started directly:

```bash
mkdir -p "$HOME/svl-data-1"
docker run -d --name simulator-8181 --gpus device=0 --user "$(id -u):$(id -g)" \
  -p 8181:8181 --add-host=host.docker.internal:host-gateway \
  -v "$PWD/LocalAssets:/assets:ro" -v "$HOME/svl-data-1:/data" \
  svlsimulator --apiport 8181
```

The library directory must already exist. Extra arguments after the image name are passed to the simulator.
