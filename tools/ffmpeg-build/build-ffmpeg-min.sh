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

# 2.6: AMD (AMF headers, MIT) and Intel Quick Sync (oneVPL dispatcher, MIT, linked statically; it loads the
# Intel runtime from the graphics driver at run time). Media Foundation needs nothing extra (Windows headers).
if [ ! -d amf ]; then
  git clone --depth 1 --filter=blob:none --sparse https://github.com/GPUOpen-LibrariesAndSDKs/AMF.git amf
  (cd amf && git sparse-checkout set amf/public/include)
fi
mkdir -p "$P/include/AMF" && cp -r amf/amf/public/include/* "$P/include/AMF/"
[ -d vpl ] || git clone --depth 1 https://github.com/intel/libvpl.git vpl
cmake -S vpl -B vpl/build -G "MSYS Makefiles" -DCMAKE_INSTALL_PREFIX="$P" -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTS=OFF \
  -DBUILD_EXAMPLES=OFF -DINSTALL_EXAMPLES=OFF -DBUILD_TOOLS=OFF -DCMAKE_BUILD_TYPE=Release
cmake --build vpl/build -j"$(nproc)"
cmake --install vpl/build

# 2.8: speech to text (ffmpeg's "whisper" filter, AI editing module). whisper.cpp + ggml are MIT and linked in
# statically, CPU only (a Vulkan / CUDA backend would make ffmpeg.exe need vulkan-1.dll / CUDA DLLs to start).
# GGML_NATIVE=OFF + AVX2/FMA/F16C: runs on any x86-64 PC from about 2013 on, not only on the build PC.
# The model file (ggml-*.bin) is not built in: the app downloads it on first use.
# CMake reads the CPU type from PROCESSOR_ARCHITECTURE; a shell started without it builds ggml's slow generic
# (no AVX) code, about 10x slower.
export PROCESSOR_ARCHITECTURE="${PROCESSOR_ARCHITECTURE:-AMD64}"
[ -d whisper ] || git clone --depth 1 https://github.com/ggml-org/whisper.cpp.git whisper
cmake -S whisper -B whisper/build -G "MSYS Makefiles" -DCMAKE_INSTALL_PREFIX="$P" -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=OFF -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF -DWHISPER_BUILD_SERVER=OFF \
  -DGGML_NATIVE=OFF -DGGML_AVX=ON -DGGML_AVX2=ON -DGGML_FMA=ON -DGGML_F16C=ON -DGGML_OPENMP=OFF
cmake --build whisper/build -j"$(nproc)"
cmake --install whisper/build
for l in ggml ggml-base ggml-cpu; do [ -f "$P/lib/$l.a" ] && cp "$P/lib/$l.a" "$P/lib/lib$l.a"; done   # (named without "lib")

[ -d ff ]   || git clone --depth 1 https://github.com/FFmpeg/FFmpeg.git ff
source "$HERE/features.sh"
cd ff
./configure --prefix="$W/out" $CONF --enable-static --disable-shared --pkg-config-flags=--static \
  --extra-cflags="-I$P/include" --extra-ldflags="-static -L$P/lib" \
  --extra-libs="-lwhisper -lggml -lggml-cpu -lggml-base -lstdc++ -lm -lcfgmgr32 -lole32 -lshlwapi -ladvapi32"
make -j"$(nproc)"
strip -o "$HERE/dist/ffmpeg.exe"  ffmpeg.exe
strip -o "$HERE/dist/ffprobe.exe" ffprobe.exe
ls -la "$HERE/dist"
