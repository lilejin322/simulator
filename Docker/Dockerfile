# Simulator runtime image: your own player build (build.sh -> build/), no display needed.
# Build from the repo root (the .dockerignore there keeps the context to build/ and Docker/):
#   ./build.sh && docker build -f Docker/Dockerfile -t svlsimulator .
FROM ubuntu:22.04

RUN set -ex \
  && apt-get update \
  && DEBIAN_FRONTEND=noninteractive apt-get install --no-install-recommends -y \
    ca-certificates \
    libgl1 \
    tini \
    libglu1-mesa \
    libgtk2.0-0 \
    libvulkan1 \
    libx11-6 \
    libxcursor1 \
    libxext6 \
    libxi6 \
    libxrandr2 \
    libxinerama1 \
  && rm -rf /var/lib/apt/lists/*

# The NVIDIA container runtime mounts the driver libraries; "graphics" is needed for Vulkan, "utility" for nvidia-smi
ENV NVIDIA_VISIBLE_DEVICES=all
ENV NVIDIA_DRIVER_CAPABILITIES=graphics,display,utility,compute,video

# Contents of https://gitlab.com/nvidia/container-images/vulkan/-/blob/dc389b0445c788901fda1d85be96fd1cb9410164/nvidia_icd.json
RUN mkdir -p /etc/vulkan/icd.d/ \
  && printf '%s\n' \
'{' \
'    "file_format_version" : "1.0.0",' \
'    "ICD": {' \
'        "library_path": "libGLX_nvidia.so.0",' \
'        "api_version" : "1.1.99"' \
'    }' \
'}' > /etc/vulkan/icd.d/nvidia_icd.json

COPY build/ /opt/simulator/
COPY Docker/config.yml /opt/simulator/config.yml
COPY Docker/entrypoint.sh /usr/local/bin/simulator-entrypoint

# Writable for any uid (docker run --user $(id -u):$(id -g)); /data is normally a host mount
RUN mkdir -p /data /tmp/home && chmod 1777 /data /tmp/home

ENV HOME=/tmp/home
# api_port default; Docker/run.sh passes --apiport HOST_PORT so it matches the host port
EXPOSE 8181
VOLUME ["/data"]
# tini as PID 1 reaps the zombies of the processes the player starts (ffmpeg, bridges)
ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/simulator-entrypoint"]
