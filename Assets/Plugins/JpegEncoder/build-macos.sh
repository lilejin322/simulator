#!/bin/sh

set -eu

git clone --depth=1 -b 2.0.2 https://github.com/libjpeg-turbo/libjpeg-turbo
cd libjpeg-turbo
cmake -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES="arm64;x86_64" \
    -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0 -DENABLE_SHARED=ON -DENABLE_STATIC=OFF -DWITH_SIMD=OFF .
cmake --build . --config Release -- -j`sysctl -n hw.ncpu`
cp -L libturbojpeg.dylib ../macOS/
cd ..
rm -rf libjpeg-turbo
install_name_tool -id @rpath/libturbojpeg.dylib macOS/libturbojpeg.dylib
codesign --force --sign - macOS/libturbojpeg.dylib
