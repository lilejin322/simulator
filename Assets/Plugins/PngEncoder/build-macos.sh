#!/bin/sh

set -eu

git clone --depth=1 -b v1.6.37 git://git.code.sf.net/p/libpng/code libpng

# the macOS SDK defines TARGET_OS_MAC but no longer has the classic Mac OS fp.h
sed -i '' 's/ || defined(TARGET_OS_MAC)//' libpng/pngpriv.h

# zlib comes with macOS; libpng 1.6.37 generates its symbol list for one architecture at a time
SDK=`xcrun --sdk macosx --show-sdk-path`
for ARCH in arm64 x86_64; do
    cmake -S libpng -B libpng/$ARCH -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_OSX_ARCHITECTURES=$ARCH -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0 -DPNG_SHARED=ON -DPNG_STATIC=OFF \
        -DPNG_TESTS=OFF -DPNG_HARDWARE_OPTIMIZATIONS=OFF -DZLIB_INCLUDE_DIR=$SDK/usr/include \
        -DZLIB_LIBRARY=$SDK/usr/lib/libz.tbd
    cmake --build libpng/$ARCH -- -j`sysctl -n hw.ncpu`
done

lipo -create libpng/arm64/libpng16.dylib libpng/x86_64/libpng16.dylib -output macOS/libpng.dylib
rm -rf libpng
install_name_tool -id @rpath/libpng.dylib macOS/libpng.dylib
codesign --force --sign - macOS/libpng.dylib
