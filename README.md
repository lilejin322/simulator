<h1 align="center">SVL Simulator:  An Autonomous Vehicle Simulator</h1>

<div align="center">
<a href="https://github.com/lilejin322/simulator/releases/latest">
<img src="https://img.shields.io/github/release-pre/lilejin322/simulator.svg" alt="GitHub release" /></a>
<a href="https://github.com/lilejin322/simulator/releases">
<img src="https://img.shields.io/github/downloads/lilejin322/simulator/total.svg" alt="Release downloads" /></a>
</div>
<div align="center">
  <h4>
    <a href="https://github.com/lilejin322/simulator/" style="text-decoration: none">
    Repository</a>
    <span> | </span>
    <a href="#building-from-source" style="text-decoration: none">
    Build instructions</a>
    <span> | </span>
    <a href="https://github.com/lilejin322/simulator/releases/latest" style="text-decoration: none">
    Download</a>
  </h4>
</div>

![SVL Simulator Offline Version](./Docs/lgsvl.png)

## Overview

This fork of SVL Simulator runs without the WISE cloud service. Maps, vehicles, sensors, bridges, NPCs, pedestrians
and controllables are compiled into the simulator from source. The simulator starts an API Only simulation by
default and accepts connections from `lgsvl` PythonAPI 2021.3 on port 8181. Existing WISE asset IDs, including those
in `lgsvl.wise.DefaultAssets`, are preserved for API compatibility.

Asset sources are distributed separately from this repository in the Hugging Face dataset
[lilejin322/svl-content](https://huggingface.co/datasets/lilejin322/svl-content), one `<category>/<folder>.tar.gz` per
source. The build script downloads the selected sources from the dataset's `unity-2020` branch into `Assets/` before
building. Runtime asset bundles are not used.

### Branches

- `unity-2020`: this project on Unity 2020.3.48f1; `build.sh` fetches its asset sources from the `unity-2020` branch of
  the content dataset.
- `main`: the same project upgraded to Unity 6000.6.7f1, fetching the dataset's `main` branch.
- `mac`: the Unity 6000.6.7f1 version for macOS as well as Linux, fetching the dataset's `mac` branch.
- `assets`: the original workflow, which downloads prebuilt asset bundles instead of building the sources in.

## Building from source

### Requirements

- Linux x86_64 with Bash and GNU command-line utilities. For macOS, use the `mac` branch.
- Unity **2020.3.48f1** with Linux build support, matching [`ProjectVersion.txt`](ProjectSettings/ProjectVersion.txt).
- A GPU and driver with Vulkan support.
- Git and Git LFS to clone the repository and retrieve its binary files.
- Xvfb (`xvfb-run`) and `xauth` for builds and headless execution.
- `curl`, `dpkg-deb` and `sha256sum` for downloading and extracting build dependencies.

### Build and start

Clone the repository with Git LFS enabled. From the repository root, edit
[`build-assets.yaml`](build-assets.yaml) to select assets, then build the Linux player:

```bash
./build.sh
```

The script locates the required Unity version in Unity Hub or unity CLI installation directories. For a custom
installation path, set `UNITY` to the editor executable:

```bash
UNITY=/path/to/Editor/Unity ./build.sh
```

The default output directory is `build/`, and the build log is written to `Logs/build-linux64.log`. A successful
build creates `build/config.yml` from [`config.yml.template`](config.yml.template) if no configuration exists.
Start the player with a window or in headless mode:

```bash
./build/simulator
# Or run without a window:
./run-headless.sh
```

Python API clients can then connect to `localhost:8181`. See [Runtime configuration](#runtime-configuration) for
API and startup settings, or [Docker](#docker) for container deployment.

Unity 2020 requires OpenSSL 1.1 compatibility libraries. Before starting the editor, `build.sh` downloads any
missing libraries from Ubuntu's package archive, verifies the package checksum, and extracts them into
`.build-tools/lib/`. Existing libraries are reused; this step does not install system packages or require sudo.
The script also enables .NET invariant globalization for the compiler.

### Asset selection

[`build-assets.yaml`](build-assets.yaml) selects source folders in seven categories: maps, vehicles, sensors,
bridges, NPCs, pedestrians and controllables. Each exact folder name has a `true` / `false` switch. The default
manifest enables the available sources and disables plugins marked as unavailable. For example:

```yaml
version: 1
maps:
  BorregasAve_4552b7ac: true
  CubeTown_0eefb0f6: true
  CubeTown_0595d9d3: false
```

The example is an excerpt: `vehicles`, `sensors`, `bridges`, `npcs`, `pedestrians` and `controllables` use the same
format. Missing entries or categories are disabled; use `{}` for an empty category. New sources are disabled until
explicitly added as `true`. Folder names are exact, including the GUID suffix: `CubeTown` does not select all seven
versions. An enabled source that is missing or not ready, a duplicate YAML key, an unknown category, or a value other
than literal `true` / `false` fails the build. Disabled sources need not be installed.

`./build.sh build-small --assets path/to/assets.yaml` selects an alternate manifest and output directory. The
`Simulator/Build...` editor menu uses the default manifest but does not download asset sources. Run
`./build.sh --fetch-only` to prepare missing sources before building in the editor. Successful builds include the
input `build-assets.yaml` and a
`build-assets.resolved.json` listing the selected source folders and full assetGuids. The editor's full catalogs are
restored after building, including on failure.

These switches control built-in asset catalogs and their player content. Unity still imports project sources and
compiles scripts; shared resources or assets referenced directly by a selected scene/prefab remain dependencies.
Turning a sensor or bridge off does not enable a replacement automatically: vehicle sensor configurations that need
it cannot be used. Keep `Library/` to reuse Unity's import and compilation caches. Runtime network/window settings
remain in `config.yml`, separate from this build manifest.

### Fetching sources

Before building, `./build.sh` downloads enabled sources whose folders are missing from `Assets/`. Each source is
extracted into `Assets/<category>/<folder>/` with its `.meta` file and the category's `README.md`.

| Command | Behavior |
|---------|----------|
| `./build.sh` | Prepare missing compatibility libraries and selected asset sources, then build the player. |
| `./build.sh --fetch-only` | Download missing selected asset sources without preparing compatibility libraries or starting Unity. |
| `./build.sh --no-fetch` | Build using existing libraries and asset sources, with script downloads disabled. Missing dependencies cause an error. |

- The dataset revision is `CONTENT_REVISION` (default `unity-2020`, the sources for this Unity 2020.3 project), resolved
  to one commit for the whole run; `CONTENT_REPO` selects another dataset and `HF_ENDPOINT` another Hugging Face server.
  `HF_TOKEN` is sent when set, which a private dataset needs.
- Each file is checked against the hash Hugging Face reports for it (sha256 for the packages) before it is unpacked. A
  package is unpacked next to its target and moved in place when complete, so an interrupted run leaves no partly
  installed source; its download continues on the next run from a hidden `.<folder>.tar.gz.partial` file.
- Installed sources are never replaced or removed, and disabled ones are not fetched. To update a source, delete its
  folder and run `./build.sh` again.
- A folder name that is not in the dataset fails before anything is downloaded.

## Asset reference

### Descriptors and sensor configurations

Each asset's descriptor is its identity and configuration source:

```
Assets/Maps/<folder>/map.json
Assets/Vehicles/<folder>/vehicle.json
Assets/Vehicles/<folder>/SensorConfigurations/<configuration>.json
Assets/Sensors/<folder>/sensor.json
Assets/Bridges/<folder>/bridge.json
```

Descriptors keep `id`, `assetGuid`, `name`, source scene/prefab/type and readiness. Optional `displayName` preserves
an existing public name when it differs from the prefab name; `description` supplies the UI description. `preferred:
true` resolves an otherwise ambiguous display name. Existing WISE IDs remain in `id` for Python API compatibility.
Attribution stays with the source in `attribution` and `LICENSE-3RD-PARTY`.

Each vehicle's `sensorConfigurations` lists relative source paths in display order, for example:

```json
"sensorConfigurations": [
  "SensorConfigurations/Apollo_6_0_12345678.json",
  "SensorConfigurations/Autoware_87654321.json"
]
```

The first entry supplies the default sensors when adding the vehicle without a configuration ID. Each referenced
file contains its own `id`, `name`, `sensors` and `bridge`; vehicle identity is inherited from `vehicle.json`.
Sensors retain `name`, `parent`, `type`, `transform`, `params` and the plugin identity (`id`, `type`, `assetGuid`).
The bridge stores its `id`, `name`, `type`, `assetGuid` and optional `connectionString`. A null bridge is allowed.
Keep IDs stable when editing a configuration so existing scripts and scenarios continue to work.

`Simulator/Refresh Built-in Asset Library`, player builds and changes to these source files generate
`Assets/Resources/BuiltinAssets.json`. This small runtime directory contains `maps`, `vehicles` (with their
configurations) and `plugins`, limited by the build manifest during a player build. The Python API resolves ids and
names through it. Loading then uses one generated table per kind, from assetGuid to map scene, prefab or bridge type:
`BuiltinMaps`, `BuiltinVehicles`, `BuiltinSensors`, `BuiltinBridges`, `BuiltinNPCs`, `BuiltinPedestrians` and
`BuiltinControllables.asset` in `Assets/Resources`. Edit the source files, not the generated files; they are not
tracked by git.
The initial identities and sensor presets were migrated from [SORA-SVL](https://github.com/YuqiHuai/SORA-SVL);
see `LICENSE-3RD-PARTY` for attribution.

### Maps

Each map lives in `Assets/Maps/<name>_<assetGuid prefix>/`; names repeat, so folder names include the first 8
characters of the assetGuid. Its `map.json` holds the WISE id, assetGuid, name, scene path and whether it is `ready`.

`Simulator/Refresh Built-in Maps` and player builds discover these descriptors and generate
`Assets/Resources/BuiltinMaps.asset`, which runtime map loading uses; a map not in it cannot be loaded. Python API ids
and names resolve through each map's descriptor in the generated asset library. Each map directory also holds its `hdmaps/` and `images/`;
Autoware vector-map archives are expanded beside their ZIPs.

Point clouds belong to their map under `PointCloud/`. The editor reads that directory directly; player builds copy the
included maps' point clouds into the player's `StreamingAssets/Maps/<assetGuid>/`.

### Vehicles

Each vehicle lives in `Assets/Vehicles/<name>_<assetGuid prefix>/` with its prefab, its own scripts in `Scripts/`,
`vehicle.json` and its sensor configurations. `Simulator/Refresh Built-in Vehicles` and player builds discover the
`vehicle.json` descriptors and generate `Assets/Resources/BuiltinVehicles.asset` for the selected vehicles. The FMU
Jaguar, `Jaguar2015XE_6761a228`, drives through an FMU library built from C source; see its `FMU/README.md`.

Three descriptors declare `runtimeOverrides`: the `2d57c96d` and `4119f973` versions of `210823_v14` lower the four
wheel mounts by 0.25 m to keep the chassis off the road; `Truck_GM_3cce4e04` places its center of mass at
`(0, 0.14, 0.12)` inside the small chassis, between the axles, to prevent tipping. `AgentManager` applies these
settings once to each instance before simulation. They keep the vehicles drivable; they are not dynamics calibration.

### Asset lookup

Assets are looked up by WISE id, assetGuid or name. When several assets share a name, descriptors with `preferred: true`
are preferred (the migrated defaults preserve the former SVL Content preference). A vehicle looked up by the id of one of its sensor configurations comes with that
configuration's sensors and bridge; looked up by name it gets the sensors of its first configuration and no bridge.

Assets marked `ready: false` (see `Assets/Sensors/README.md` and `Assets/Bridges/README.md`) are not built in and
cannot be used: the
ROS2ForUnitySVLBridge, which 43 sensor configurations use, two author-specific ROS bridges (`386670e5`, `568f6162`)
and the ImuSensor `88c08315`, DoorStateSensor and AutoDriveCarStateSensor plugins. A simulation asking for one fails
with "... is not built into the simulator". Asset bundles are not loaded, neither ones built with `Simulator/Build...`
nor the `AssetBundles` folder of LG releases.

## Runtime configuration

`config.yml` next to the simulator executable (or in the project folder when running in the Editor):

```yaml
api_hostname: "localhost"
api_port: 8181
start_api_only: true            # start an API Only simulation on launch
```

The command line option `--no-api-only` overrides `start_api_only`. `assets_path` in older config files is ignored.

A simulator built without the asset sources has no assets and does not start the Python API: the loader screen says
so, and in batch mode (`run-headless.sh`) the simulator exits with code 1.

### Headless

`run-headless.sh [path/to/simulator] [arguments...]` runs the simulator without a window: Unity batch mode on a virtual
X server (`xvfb-run`), rendering with Vulkan on the GPU. Unity 2020.3 on Linux needs an X server even in batch mode.
In batch mode, or with `headless: true`, the simulation renders the sensors only, not the main view.

### Loader screen

With `start_api_only: false` the loader screen has a Start API button next to a port field showing `api_port`, an
"Allow remote" checkbox (listen on all network interfaces) and a "Headless" checkbox showing `headless`. What is
chosen there applies to that run only and is not written back to `config.yml`.

## Docker

The runtime image packages the Linux player and its selected assets with Xvfb. Build the player before building
the image:

```bash
./build.sh
docker build -f Docker/Dockerfile -t svlsimulator .
Docker/run.sh 8181 0 ~/svl-data
```

The example exposes the Python API on port 8181, selects GPU 0, and stores runtime data in `~/svl-data`.
Container hosts need an NVIDIA driver and the NVIDIA Container Toolkit; Unity is only needed to build the player.
See the [Docker guide](Docker/README.md) for configuration, multiple instances and bridge connections.

## Project background

SVL Simulator was developed by LG Electronics America R&D Lab as a Unity HDRP simulator for autonomous driving
and robotics. LG suspended active development of the original project on January 1, 2022. This fork provides
source-based asset packaging and operation without WISE; the original cloud-linking workflow does not apply.

## Citation

Research using SVL Simulator should cite the ITSC 2020 paper:
[LGSVL Simulator: A High Fidelity Simulator for Autonomous Driving](https://arxiv.org/pdf/2005.03778)

```
@article{rong2020lgsvl,
  title={LGSVL Simulator: A High Fidelity Simulator for Autonomous Driving},
  author={Rong, Guodong and Shin, Byung Hyun and Tabatabaee, Hadi and Lu, Qiang and Lemke, Steve and Mo{\v{z}}eiko, M{\=a}rti{\c{n}}{\v{s}} and Boise, Eric and Uhm, Geehoon and Gerow, Mark and Mehta, Shalin and others},
  journal={arXiv preprint arXiv:2005.03778},
  year={2020}
}
```

## Copyright and License

Copyright (c) 2019-2021 LG Electronics, Inc.

See [LICENSE](LICENSE) for the software license and [LICENSE-3RD-PARTY](LICENSE-3RD-PARTY) for third-party
licenses and attribution. Asset sources also include their own attribution and license information.
