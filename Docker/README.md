# Running the simulator in Docker (no display needed)

The image contains your own player build (`build.sh` -> `build/`) plus Xvfb. Inside the container the simulator
starts on a private virtual X screen (`xvfb-run -a`, needed by Unity 2020.3 on Linux) while rendering runs on the
GPU through Vulkan. No host X server, `DISPLAY` or X socket is used. The asset library is not in the image; it is
mounted from the host.

## Requirements (each host)

- NVIDIA driver and the NVIDIA Container Toolkit (`docker run --gpus` must work)
- The local asset library (`LocalAssets/`, ~11 GB) somewhere on the host

## Build the image

On a build machine (Unity needed only here):

    ./build.sh
    docker build -f Docker/Dockerfile -t svlsimulator .

Run from the repo root. The root `.dockerignore` only lets `build/` and `Docker/` into the build context
(debug symbols `build/*.debug` and the developer `build/config.yml` are left out). Then push the image to a
registry or `docker save | docker load` it on the other hosts.

## Container layout

### What is inside the image

| path in container | what it is |
|-------------------|------------|
| `/opt/simulator/` | the player build, copied from `build/` at image build time. `/opt/simulator/simulator` is the executable |
| `/opt/simulator/config.yml` | the container's config, copied from `Docker/config.yml` (see below). Your own `build/config.yml` is not used |
| `/usr/local/bin/simulator-entrypoint` | `Docker/entrypoint.sh`, the start script (runs under `tini`) |
| `/tmp/home` | `HOME` inside the container, writable by any user. Unity keeps its own settings here; they are thrown away with the container |

`/opt` is the usual Linux place for self-contained third-party software, and the same path the original SVL image used.

### What comes from the host (mounts)

Nothing you need to keep lives in the image. Two host folders are mounted in at fixed paths:

| host folder (run.sh argument) | path in container | access | content |
|-------------------------------|-------------------|--------|---------|
| ASSETS_DIR, e.g. `/local/lilejin/simulator/LocalAssets` | `/assets` | read-only | asset library: `maps/`, `vehicles/`, `plugins/` bundles and `metadata/*.json` |
| DATA_DIR, e.g. `~/svl-data-1` | `/data` | read-write | everything the simulator writes: `data.db` (+ `backup.db`), downloaded/cached bundles under `Sensors/`, `Vehicles/`, logs, saved sensor images and recorded videos |

The container runs as your own user (`--user $(id -u):$(id -g)`), so files in DATA_DIR are owned by you on the host and
stay there after the container is removed. Use one DATA_DIR per running instance: the simulator locks its database
(`run.lock`), two instances on the same folder conflict. ASSETS_DIR can be shared by all instances.

### Container config (`Docker/config.yml`)

| key | value | why |
|-----|-------|-----|
| `api_hostname` | `"*"` | listen on all interfaces inside the container, so the published port is reachable from outside |
| `api_port` | `8181` | default only; `run.sh` overrides it with `--apiport HOST_PORT` |
| `assets_path` | `/assets` | the mounted asset library |
| `data_path` | `/data` | the mounted data folder |
| `headless` | `true` | render sensors only, no main view (always the case in batch mode anyway) |
| `start_api_only` | `true` | start an API Only simulation on launch so Python clients can connect right away |

### Start command

The entrypoint runs:

    xvfb-run -a -s "-screen 0 1280x720x24" /opt/simulator/simulator \
        -batchmode -force-vulkan -logFile - --data /data [your arguments]

`xvfb-run -a` starts a private virtual X screen inside this container and stops it with the simulator; nothing is
needed on the host. `-logFile -` sends the Unity log to `docker logs`. The harmless startup line
`Unknown argument -force-vulkan, skipping it` comes from the simulator's own argument parser; Vulkan is still used.

### Ports

The only port to publish is the Python API WebSocket port. Host and container port must be the same number
(`-p 8182:8182 --apiport 8182`, which `run.sh` does for you), because the API server rejects connections whose port
differs from its own. Each container has its own network, so several instances never clash. UDP port 9999 is only
used by multi-machine cluster simulations and is not published. Outgoing connections (bridges, internet, other
machines) need no configuration.

### Full example

    Docker/run.sh --name sim-a 8181 0 /local/lilejin/simulator/LocalAssets ~/svl-data-1

is the same as:

    docker run -d --name sim-a --gpus device=0 --user $(id -u):$(id -g) \
      -p 8181:8181 --add-host=host.docker.internal:host-gateway \
      -v /local/lilejin/simulator/LocalAssets:/assets:ro \
      -v ~/svl-data-1:/data \
      svlsimulator --apiport 8181

| on the host | in the container |
|-------------|------------------|
| port 8181 | port 8181 (Python API) |
| GPU 0 in `nvidia-smi` | the only GPU visible |
| `/local/lilejin/simulator/LocalAssets` | `/assets` (read-only) |
| `~/svl-data-1` | `/data` |
| your user lilejin | same uid/gid |
| `host.docker.internal` | resolves to the host (for bridges) |

## Run

    Docker/run.sh [--name NAME] HOST_PORT GPU ASSETS_DIR DATA_DIR [simulator arguments...]

| parameter  | meaning |
|------------|---------|
| `--name`   | container name, optional; default `simulator-HOST_PORT` so instances never collide |
| HOST_PORT  | Python API port on the host. The container listens on the same port (`-p PORT:PORT --apiport PORT`): the API server rejects connections whose `Host` port differs from its own, so a `-p 18181:8181` style remap does not work |
| GPU        | GPU index as in `nvidia-smi` (`--gpus device=N`); the container only sees that GPU |
| ASSETS_DIR | asset library, mounted read-only at `/assets` |
| DATA_DIR   | mounted at `/data`: database, cache, logs, saved sensor images/videos. Created if missing |

The container runs as the calling user (`--user $(id -u):$(id -g)`), so everything in DATA_DIR belongs to you.

Several instances on one host, each with its own port and data folder:

    Docker/run.sh 8181 0 /path/to/LocalAssets ~/svl-data-1
    Docker/run.sh --name sim-b 8182 1 /path/to/LocalAssets ~/svl-data-2

Then connect with the Python API (`lgsvl.Simulator("HOST", 8181)`). Logs: `docker logs -f simulator-8181`.
Stop: `docker rm -f simulator-8181`.

Note: the `lgsvl` PythonAPI from GitHub needs `websockets` < 11 (e.g. `pip install websockets==10.4`); newer
versions fail the handshake.

## Reaching services on the host (ROS / Apollo / Autoware bridges)

Inside the container `localhost` is the container itself, not the host. `run.sh` adds
`--add-host=host.docker.internal:host-gateway`, so address bridges running on the host as
`host.docker.internal` (or by the host's IP), e.g. in the bridge connection string `host.docker.internal:9090`.
Do not use `--net=host`: all instances would then share the host network and the per-instance ports clash.

## Without run.sh

    docker run -d --name simulator-8181 --gpus device=0 --user $(id -u):$(id -g) \
      -p 8181:8181 --add-host=host.docker.internal:host-gateway \
      -v /path/to/LocalAssets:/assets:ro -v ~/svl-data-1:/data \
      svlsimulator --apiport 8181

Extra arguments after the image name go to the simulator (e.g. `--no-api-only`).
