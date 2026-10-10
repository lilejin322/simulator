<h1 align="center">SVL Simulator:  An Autonomous Vehicle Simulator</h1>

<div align="center">
<a href="https://github.com/lgsvl/simulator/releases/latest">
<img src="https://img.shields.io/github/release-pre/lgsvl/simulator.svg" alt="Github release" /></a>
<a href="">
<img src="https://img.shields.io/github/downloads/lgsvl/simulator/total.svg" alt="Release downloads" /></a>
</div>
<div align="center">
  <h4>
    <a href="https://svlsimulator.com" style="text-decoration: none">
    Website</a>
    <span> | </span>
    <a href="https://svlsimulator.com/docs" style="text-decoration: none">
    Documentation</a>
    <span> | </span>
    <a href="https://github.com/lgsvl/simulator/releases/latest" style="text-decoration: none">
    Download</a>
  </h4>
</div>

## Running without WISE

This fork runs without the WISE cloud service, which was shut down in 2022. Maps, vehicles and plugins come from a
local asset library folder instead, and the simulator starts an API Only simulation on launch, so Python API scripts
(`lgsvl` PythonAPI 2021.3) connect to port 8181 and work unchanged, including the WISE ids in `lgsvl.wise.DefaultAssets`.

### Local asset library

```
<assets_path>/
  maps/           map bundles, named by assetGuid
  vehicles/       vehicle bundles
  plugins/        sensor and bridge plugin bundles
  metadata/       WISE records (*.json): names, ids and vehicle sensor configurations
  npcs/           NPC vehicle bundles, loaded at startup (subfolders allowed)
  pedestrians/    pedestrian bundles, loaded at startup
  controllables/  controllable bundles, loaded at startup
```

Maps, vehicles and plugins are loaded when a simulation asks for them; NPCs, pedestrians and controllables are loaded
when the simulator starts, since every simulation may use them.

Records in `metadata/*.json` are arrays of `{"cid": <WISE id>, "ctype": "vehicle" | "configuration", "endpoint": "/api/v1/<maps|vehicles|plugins>/<id>", "data": {...}}`,
the format used by [SORA-SVL](https://github.com/YuqiHuai/SORA-SVL). The bundles and the `maps.json`, `vehicles.json`
and `plugins.json` records published by that project make a complete library. Bundles without a record are still listed,
under the name from their manifest; bundles built with `Simulator/Build...` can be copied into the matching folder as is.

Assets are looked up by WISE id, assetGuid or name. When several assets share a name, the ones published by
"SVL Content" are preferred. A vehicle looked up by the id of one of its sensor configurations comes with that
configuration's sensors and bridge; looked up by name it gets the sensors of its first configuration and no bridge.

The NPC, pedestrian and controllable bundles of the official 2021.3 release are in its `AssetBundles` folder; copy its
`NPCs`, `Pedestrians` and `Controllables` folders into `npcs/`, `pedestrians/` and `controllables/`. The simulator still
loads bundles from the `AssetBundles` folder next to its executable too, as LG releases did; an asset present in both
places is loaded once, from the library.

### Configuration

`config.yml` next to the simulator executable (or in the project folder when running in the Editor):

```yaml
api_hostname: "localhost"
api_port: 8181
assets_path: "/path/to/assets"   # relative paths are relative to the simulator folder, default "LocalAssets"
start_api_only: true            # start an API Only simulation on launch
```

The command line options `--assets <path>` and `--no-api-only` and the `SIMULATOR_ASSETS` environment variable override it.
Bundles are linked (or copied on Windows) into the data folder when used; clearing the cache never touches the library.

## Sunsetting SVL Simulator

### LG has made the difficult decision to suspend active development of SVL Simulator, as of January 1, 2022.

There are no plans for any new or bugfix releases of SVL Simulator in 2022. There will be no new source code changes pushed to GitHub, and there will be no reviews or merging of Pull Requests submitted on GitHub for SVL Simulator or any of its related plug-ins. There will be no new simulator assets built and shared nor updates made to existing assets by the SVL Simulator team.

We will make a reasonable effort to keep the [wise.svlsimulator.com](https://wise.svlsimulator.com) website up and running through at least Thursday, June 30, 2022, and will also maintain the [SVLSimulator.com](https://SVLSimulator.com) web site including the [simulator documentation](https://www.svlsimulator.com/docs/) pages through at least Thursday, June 30, 2022.

At this time we do not plan to remove the [SVL Simulator source code](https://github.com/lgsvl/simulator) (and related plug-in) projects from GitHub. The open source project and source code will remain available for anyone who wants to build (or modify) SVL Simulator.

We apologize for any inconvenience this may cause and appreciate your understanding.

Sincerely,

The SVL Simulator team

<br/>

### Questions

##### **Q: Will SVL Simulator run locally without the [wise.svlsimulator.com](https://wise.svlsimulator.com) web site?**

A: The latest released version of SVL (2021.3) requires the use of the WISE (web user interface) cloud service to download assets, configure simulation clusters, and create and launch simulations. It may be possible to remove this dependency (e.g. in a fork; see below) and then build and launch a local simulator instance.

##### **Q: Will LG release the source code for the WISE (web user interface) cloud service?**

A: There are no plans at this time to release the source code for WISE.

##### **Q: Would LG be interested in transferring the SVL Simulator project to someone interested in taking over the project?**

A: There are no plans at this time to transfer the SVL Simulator project (or the WISE cloud service).

##### **Q: Will the Simulator Software License Agreement be changed?**

A: There are no plans at this time to change the [Simulator Software License Agreement](https://github.com/lgsvl/simulator/blob/master/LICENSE).

##### **Q: Can users share source code changes and forks of SVL Simulator?**

A: As a GitHub project, anyone is able to create their own fork; in fact, there are already over 500 such forks of the [SVL Simulator project](https://github.com/lgsvl/simulator). Users can then make changes and even submit pull requests back to the original (parent) project. While we have no plans to review or merge such changes into the main project, users are free to review open PRs and create their own local build from their own or any other public fork.

##### **Q: Can users share new builds of SVL Simulator?**

A: The [Simulator Software License Agreement](https://github.com/lgsvl/simulator/blob/master/LICENSE) allows you to “modify or create derivative works of the Licensed Materials” and only restricts their commercial use. Therefore, it is ok for users to share new builds of SVL Simulator as long as such builds are not sold or otherwise used for commercial purposes.

##### **Q: What about the SVL Simulator Premium product and/or cloud simulation service?**

A: There are no plans at this time to offer a SVL Simulator Premium product or cloud service.

##### **Q: Can users still post technical questions on GitHub?**

A: Technical questions may be posted to the [SVL Simulator Issues](https://github.com/lgsvl/simulator/issues) page on GitHub but may not be answered by the SVL Simulator team. We encourage users to help each other with questions or issues that are posted.

##### **Q: What if there are other questions not addressed in this document?**

A: Other questions may be posted to the [SVL Simulator Issues](https://github.com/lgsvl/simulator/issues) page on GitHub.

<br/>

------ 
# Project README

## Stay Informed

Check out our latest [news](https://www.svlsimulator.com/news/) and subscribe to our [mailing list](http://eepurl.com/htlRjH) to get the latest updates.


## Introduction

LG Electronics America R&D Lab has developed an HDRP Unity-based multi-robot simulator for autonomous vehicle developers. 
We provide an out-of-the-box solution which can meet the needs of developers wishing to focus on testing their autonomous vehicle algorithms. 
It currently has integration with The Autoware Foundation's [Autoware.auto](https://gitlab.com/autowarefoundation/autoware.auto/AutowareAuto) and Baidu's [Apollo](https://github.com/ApolloAuto/apollo) platforms, can generate HD maps, and can be immediately used for testing and validation of a whole system with little need for custom integrations. 
We hope to build a collaborative community among robotics and autonomous vehicle developers by open sourcing our efforts. 

*To use the simulator with Apollo 6.0 or master, first download the simulator binary, then follow our [Running with latest Apollo](https://www.svlsimulator.com/docs/system-under-test/apollo-master-instructions/) docs.*

*To use the simulator with Autoware.auto, first download the simulator binary, then follow the guide on our [Autoware.auto](https://autowarefoundation.gitlab.io/autoware.auto/AutowareAuto/lgsvl.html).*

For users in China, you can view our latest videos [here](https://space.bilibili.com/412295691) and download our simulator releases [here](https://pan.baidu.com/s/1M33ysJYZfi4vya41gmB0rw) (code: 6k91).
对于中国的用户，您也可在[哔哩哔哩](https://space.bilibili.com/412295691)上观看我们最新发布的视频，从[百度网盘](https://pan.baidu.com/s/1M33ysJYZfi4vya41gmB0rw)(提取码: 6k91)上下载使用我们的仿真器。


## Paper
If you are using SVL Simulator for your research paper, please cite our ITSC 2020 paper:
[LGSVL Simulator: A High Fidelity Simulator for Autonomous Driving](https://arxiv.org/pdf/2005.03778)

```
@article{rong2020lgsvl,
  title={LGSVL Simulator: A High Fidelity Simulator for Autonomous Driving},
  author={Rong, Guodong and Shin, Byung Hyun and Tabatabaee, Hadi and Lu, Qiang and Lemke, Steve and Mo{\v{z}}eiko, M{\=a}rti{\c{n}}{\v{s}} and Boise, Eric and Uhm, Geehoon and Gerow, Mark and Mehta, Shalin and others},
  journal={arXiv preprint arXiv:2005.03778},
  year={2020}
}
```



## Getting Started

You can find complete and the most up-to-date guides on our [documentation website](https://www.svlsimulator.com/docs).

Running the simulator with reasonable performance and frame rate (for perception related tasks) requires a high performance desktop. Below is the recommended system for running the simulator at high quality. We are currently working on performance improvements for a better experience. 

**Recommended system:**

- 4 GHz Quad Core CPU
- Nvidia GTX 1080, 8GB GPU memory
- Windows 10 64 Bit

The easiest way to get started with running the simulator is to download our [latest release](https://github.com/lgsvl/simulator/releases/latest) and run as a standalone executable.

Currently, running the simulator in Windows yields better performance than running on Linux. 

### Downloading and starting simulator

1. Download the latest release of the SVL Simulator for your supported operating system (Windows or Linux) here: [https://github.com/lgsvl/simulator/releases/latest](https://github.com/lgsvl/simulator/releases/latest)
2. Unzip the downloaded folder and run the executable.

See the full installation guide [here](https://svlsimulator.com/docs/installation-guide/installing-simulator).

### Building and running from source

If you would like to customize the simulator, build simulation content, or access specific features available in [Developer Mode](https://www.svlsimulator.com/docs/running-simulations/developer-mode), you can clone the project with Unity Editor, and build the project from source.

Check out our instructions for getting started with building from source [here](https://www.svlsimulator.com/docs/installation-guide/build-instructions).
**Note:** Please checkout the "release-*" branches or release tags for stable (ready features) and "master" branch for unstable (preview of work in progress).


## Simulator Instructions (from 2021.1 release onwards)

1. After starting the simulator, you should see a button to "Link to Cloud".
2. Use this button to link your local simulator instance to a [cluster](https://www.svlsimulator.com/docs/user-interface/web/clusters-tab) on our [web user interface](https://wise.svlsimulator.com).
3. Now create a [random traffic](https://www.svlsimulator.com/docs/creating-scenarios/random-traffic-scenarios/) simulation. For a standard setup, select "BorregasAve" for map and "Jaguar2015XE with Apollo 5.0 sensor configuration" for vehicle. Click "Run" to begin.
4. The vehicle should spawn inside the map environment that was selected.
5. Read [here](https://www.svlsimulator.com/docs/user-interface/keyboard-shortcuts/) for an explanation of all current keyboard shortcuts and controls.
6. Follow the guides on our respective [Autoware](https://github.com/lgsvl/Autoware) and [Apollo 5.0](https://github.com/lgsvl/apollo-5.0) repositories for instructions on running the platforms with the simulator.

**NOTE**: If using a release older than "2021.1", please follow the instructions on our documentation [archives](https://www.svlsimulator.com/docs/archive/).

### Guide to simulator functionality

Look [here](https://www.svlsimulator.com/docs) for a guide to currently available functionality and features.



## Contact

Please feel free to provide feedback or ask questions by creating a Github issue. For inquiries about collaboration, please email us at [contact@svlsimulator.com](mailto:contact@svlsimulator.com).



## Copyright and License

Copyright (c) 2019-2021 LG Electronics, Inc.

This software contains code licensed as described in LICENSE.
