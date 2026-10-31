#!/bin/sh

set -eu

git clone --depth=1 -b 3.4.1 https://github.com/LASzip/LASzip
cd LASzip
cmake -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES="arm64;x86_64" \
    -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0 .
cmake --build . -- -j`sysctl -n hw.ncpu`
cp -L lib/liblaszip.dylib ../macOS/

cd ..
rm -rf LASzip
install_name_tool -id @rpath/liblaszip.dylib macOS/liblaszip.dylib
codesign --force --sign - macOS/liblaszip.dylib
