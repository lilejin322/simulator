# Running the simulator in Docker (no display needed)

The image packages the Linux player produced by `build.sh` in `build/`. The simulator runs in batch mode,
rendering on the GPU through Vulkan; Unity 6 needs no X server for that, so no host X server, `DISPLAY` or X socket
is used. Maps, vehicles, sensors, bridges, NPCs,
pedestrians and controllables selected in `build-assets.yaml` are built into the player. Image size depends on
the selected assets.

## Requirements

- Docker Engine.
- An NVIDIA GPU, driver and the NVIDIA Container Toolkit (`docker run --gpus` must work).
- A Linux player build in `build/` to create the image. Unity is required to build the player, but not to run the image.

## Build the image

From the repository root:

    ./build.sh
    docker build -f Docker/Dockerfile -t svlsimulator .

The root `.dockerignore` limits the build context to `build/` and `Docker/`, excluding debug symbols
(`build/*.debug`) and `build/config.yml`. To deploy on another host, distribute the image through a container
registry or with `docker save` and `docker load`.

## Container layout

### What is inside the image

| path in container | what it is |
|-------------------|------------|
| `/opt/simulator/` | the player build, copied from `build/` at image build time. `/opt/simulator/simulator` is the executable |
| `/opt/simulator/config.yml` | container configuration, copied from `Docker/config.yml` instead of `build/config.yml` |
| `/usr/local/bin/simulator-entrypoint` | `Docker/entrypoint.sh`, the start script (runs under `tini`) |
| `/tmp/home` | writable home directory for Unity settings; removed with the container |

### What comes from the host (mounts)

Runtime data is persisted in a host directory mounted at `/data`:

| host folder (run.sh argument) | path in container | access | content |
|-------------------------------|-------------------|--------|---------|
| DATA_DIR, e.g. `~/svl-data-1` | `/data` | read-write | everything the simulator writes: `data.db` (+ `backup.db`), logs, saved sensor images and recorded videos |

The container runs with the calling user's UID and GID (`--user $(id -u):$(id -g)`), preserving file ownership in
DATA_DIR after the container is removed. Use one DATA_DIR per running instance: the simulator locks its database
(`run.lock`), two instances on the same folder conflict.

### Container config (`Docker/config.yml`)

| key | value | why |
|-----|-------|-----|
| `api_hostname` | `"*"` | listen on all interfaces inside the container, so the published port is reachable from outside |
| `api_port` | `8181` | default only; `run.sh` overrides it with `--apiport HOST_PORT` |
| `data_path` | `/data` | the mounted data folder |
| `headless` | `true` | render sensors only, no main view (always the case in batch mode anyway) |
| `start_api_only` | `true` | start an API Only simulation on launch so Python clients can connect right away |

### Start command

The entrypoint runs:

    /opt/simulator/simulator -batchmode -force-vulkan -logFile - --data /data [your arguments]

`-logFile -` sends the Unity log to `docker logs`. The startup line
`Unknown argument -force-vulkan, skipping it` comes from the simulator's own argument parser; Vulkan is still used.

### Ports

The only port to publish is the Python API WebSocket port. Host and container port must be the same number
(`-p 8182:8182 --apiport 8182`, which `run.sh` does for you), because the API server rejects connections whose port
differs from its own. Use a different host port for each instance. UDP port 9999 is only
used by multi-machine cluster simulations and is not published. Outgoing connections (bridges, internet, other
machines) need no configuration.

### Full example

    Docker/run.sh --name sim-a 8181 0 ~/svl-data-1

is the same as:

    docker run -d --name sim-a --gpus device=0 --user $(id -u):$(id -g) \
      -p 8181:8181 --add-host=host.docker.internal:host-gateway \
      -v ~/svl-data-1:/data \
      svlsimulator --apiport 8181

| on the host | in the container |
|-------------|------------------|
| port 8181 | port 8181 (Python API) |
| GPU 0 in `nvidia-smi` | the only GPU visible |
| `~/svl-data-1` | `/data` |
| calling user's UID and GID | same UID and GID |
| `host.docker.internal` | resolves to the host (for bridges) |

## Run

    Docker/run.sh [--name NAME] HOST_PORT GPU DATA_DIR [simulator arguments...]

| parameter  | meaning |
|------------|---------|
| `--name`   | container name, optional; default `simulator-HOST_PORT` |
| HOST_PORT  | Python API port on the host. The container listens on the same port (`-p PORT:PORT --apiport PORT`): the API server rejects connections whose `Host` port differs from its own, so a `-p 18181:8181` style remap does not work |
| GPU        | GPU index as in `nvidia-smi` (`--gpus device=N`); the container only sees that GPU |
| DATA_DIR   | mounted at `/data`: database, cache, logs, saved sensor images/videos. Created if missing |

Several instances on one host, each with its own port and data folder:

    Docker/run.sh 8181 0 ~/svl-data-1
    Docker/run.sh --name sim-b 8182 1 ~/svl-data-2

Then connect with the Python API (`lgsvl.Simulator("HOST", 8181)`). Logs: `docker logs -f simulator-8181`.
Stop: `docker rm -f simulator-8181`.

Note: the `lgsvl` PythonAPI from GitHub needs `websockets` < 11 (e.g. `pip install websockets==10.4`); newer
versions fail the handshake.

## Reaching services on the host (ROS / Apollo / Autoware bridges)

Inside the container `localhost` is the container itself, not the host. `run.sh` adds
`--add-host=host.docker.internal:host-gateway`, so address bridges running on the host as
`host.docker.internal` (or by the host's IP), e.g. in the bridge connection string `host.docker.internal:9090`.
These examples use Docker bridge networking with explicit port publishing.

## Without run.sh

    docker run -d --name simulator-8181 --gpus device=0 --user $(id -u):$(id -g) \
      -p 8181:8181 --add-host=host.docker.internal:host-gateway \
      -v ~/svl-data-1:/data \
      svlsimulator --apiport 8181

Extra arguments after the image name go to the simulator (e.g. `--no-api-only`).
