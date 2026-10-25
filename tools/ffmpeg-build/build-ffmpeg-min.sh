#!/bin/bash
# Builds a slim ffmpeg.exe + ffprobe.exe (static, LGPL, only what Clip Shrinker uses).
# Run inside the "MSYS2 MINGW64" shell on Windows (https://www.msys2.org), or let the GitHub workflow do it.
set -e
pacman -S --needed --noconfirm base-devel git autoconf automake libtool \
  mingw-w64-x86_64-toolchain mingw-w64-x86_64-nasm mingw-w64-x86_64-pkgconf mingw-w64-x86_64-zlib mingw-w64-x86_64-dav1d
HERE="$(cd "$(dirname "$0")" && pwd)"
W="$HERE/work"; P="$W/inst"; mkdir -p "$W" "$HERE/dist"; cd "$W"
export PKG_CONFIG_PATH="$P/lib/pkgconfig"

[ -d nvh ]  || git clone --depth 1 https://github.com/FFmpeg/nv-codec-headers.git nvh
(cd nvh && make install PREFIX="$P")

[ -d zimg ] || git clone --depth 1 --recurse-submodules https://github.com/sekrit-twc/zimg.git zimg
(cd zimg && ./autogen.sh && ./configure --prefix="$P" --disable-shared --enable-static && make -j"$(nproc)" && make install)

[ -d ff ]   || git clone --depth 1 https://github.com/FFmpeg/FFmpeg.git ff
source "$HERE/features.sh"
cd ff
./configure --prefix="$W/out" $CONF --enable-static --disable-shared --pkg-config-flags=--static \
  --extra-ldflags="-static" --extra-libs="-lstdc++ -lm"
make -j"$(nproc)"
strip -o "$HERE/dist/ffmpeg.exe"  ffmpeg.exe
strip -o "$HERE/dist/ffprobe.exe" ffprobe.exe
ls -la "$HERE/dist"
