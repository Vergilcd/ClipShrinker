#!/bin/bash
# Builds a slim ffmpeg.exe + ffprobe.exe (static, LGPL, only what Clip Shrinker uses).
# Run inside the "MSYS2 MINGW64" shell on Windows (https://www.msys2.org), or let the GitHub workflow do it.
set -e
pacman -S --needed --noconfirm base-devel git autoconf automake libtool \
  mingw-w64-x86_64-toolchain mingw-w64-x86_64-nasm mingw-w64-x86_64-pkgconf mingw-w64-x86_64-zlib mingw-w64-x86_64-dav1d \
  mingw-w64-x86_64-cmake
HERE="$(cd "$(dirname "$0")" && pwd)"
W="$HERE/work"; P="$W/inst"; mkdir -p "$W" "$HERE/dist"; cd "$W"
export PKG_CONFIG_PATH="$P/lib/pkgconfig"

[ -d nvh ]  || git clone --depth 1 https://github.com/FFmpeg/nv-codec-headers.git nvh
(cd nvh && make install PREFIX="$P")

[ -d zimg ] || git clone --depth 1 --recurse-submodules https://github.com/sekrit-twc/zimg.git zimg
(cd zimg && ./autogen.sh && ./configure --prefix="$P" --disable-shared --enable-static && make -j"$(nproc)" && make install)

# OpenCL (GPU denoise, nlmeans_opencl). The Khronos loader is linked in statically, so ffmpeg.exe still starts on a
# PC without OpenCL.dll; only the GPU filter fails there and the app falls back to the CPU one.
[ -d oclh ] || git clone --depth 1 https://github.com/KhronosGroup/OpenCL-Headers.git oclh
[ -d ocll ] || git clone --depth 1 https://github.com/KhronosGroup/OpenCL-ICD-Loader.git ocll
cmake -S oclh -B oclh/build -G "MSYS Makefiles" -DCMAKE_INSTALL_PREFIX="$P" -DBUILD_TESTING=OFF
cmake --install oclh/build
cmake -S ocll -B ocll/build -G "MSYS Makefiles" -DCMAKE_INSTALL_PREFIX="$P" -DCMAKE_PREFIX_PATH="$P" \
  -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTING=OFF -DCMAKE_BUILD_TYPE=Release
cmake --build ocll/build -j"$(nproc)"
cmake --install ocll/build
[ -f "$P/lib/OpenCL.a" ] && cp "$P/lib/OpenCL.a" "$P/lib/libOpenCL.a"   # (the loader names its static lib OpenCL.a)

[ -d ff ]   || git clone --depth 1 https://github.com/FFmpeg/FFmpeg.git ff
source "$HERE/features.sh"
cd ff
./configure --prefix="$W/out" $CONF --enable-static --disable-shared --pkg-config-flags=--static \
  --extra-cflags="-I$P/include" --extra-ldflags="-static -L$P/lib" \
  --extra-libs="-lstdc++ -lm -lcfgmgr32 -lole32 -lshlwapi -ladvapi32"
make -j"$(nproc)"
strip -o "$HERE/dist/ffmpeg.exe"  ffmpeg.exe
strip -o "$HERE/dist/ffprobe.exe" ffprobe.exe
ls -la "$HERE/dist"
