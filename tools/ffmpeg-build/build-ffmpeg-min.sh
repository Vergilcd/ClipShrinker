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

# 2.8: speech to text (ffmpeg's "whisper" filter, AI editing module only; the app's own ffmpeg is built without it).
# 2.8.0-beta2: whisper.cpp + ggml are DLLs next to ffmpeg.exe, and ggml's backends are loaded at run time
# (GGML_BACKEND_DL, ffmpeg calls ggml_backend_load_all): ggml-vulkan.dll runs whisper on the graphics card (NVIDIA,
# AMD, Intel); on a PC without vulkan-1.dll it does not load and the CPU backend does the work. GGML_CPU_ALL_VARIANTS
# builds one ggml-cpu-*.dll per CPU generation and ggml picks the best one, so no AVX level is fixed at build time.
# -static in the DLLs' link flags: libstdc++ / libgcc / winpthread are built in (no MinGW runtime DLLs to ship);
# --exclude-libs keeps their symbols out of the DLLs' exports (else ggml-base.dll exports _Unwind_Resume and every
# backend fails to link with "multiple definition"). Name the libraries: "ALL" would also hide each DLL's own
# functions, which CMake links from an archive (objects.a). libparakeet.dll (also built) is not used by ffmpeg.
# The model file (ggml-*.bin) is not built in: the app downloads it.
# CMake reads the CPU type from PROCESSOR_ARCHITECTURE; a shell started without it builds ggml's slow generic code.
# Needs the Vulkan headers, loader and shader compiler (glslc) at build time:
pacman -S --needed --noconfirm mingw-w64-x86_64-vulkan-headers mingw-w64-x86_64-vulkan-loader mingw-w64-x86_64-shaderc \
  mingw-w64-x86_64-spirv-headers
export PROCESSOR_ARCHITECTURE="${PROCESSOR_ARCHITECTURE:-AMD64}"
PA="$W/inst-ai"
[ -d whisper ] || git clone --depth 1 https://github.com/ggml-org/whisper.cpp.git whisper
cmake -S whisper -B whisper/build-dl -G "MSYS Makefiles" -DCMAKE_INSTALL_PREFIX="$PA" -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=ON -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF -DWHISPER_BUILD_SERVER=OFF \
  -DGGML_NATIVE=OFF -DGGML_BACKEND_DL=ON -DGGML_CPU_ALL_VARIANTS=ON -DGGML_VULKAN=ON -DGGML_OPENMP=OFF \
  -DCMAKE_SHARED_LINKER_FLAGS="-static -Wl,--exclude-libs,libgcc.a:libgcc_eh.a:libstdc++.a:libwinpthread.a" -DCMAKE_MODULE_LINKER_FLAGS="-static -Wl,--exclude-libs,libgcc.a:libgcc_eh.a:libstdc++.a:libwinpthread.a"
cmake --build whisper/build-dl -j"$(nproc)"
cmake --install whisper/build-dl
# ffmpeg links statically (-static), which only takes lib*.a: the DLLs' import libraries are copied under that name
mkdir -p "$PA/implib"
for l in whisper ggml ggml-base; do cp "$PA/lib/lib$l.dll.a" "$PA/implib/lib$l.a"; done

[ -d ff ]   || git clone --depth 1 https://github.com/FFmpeg/FFmpeg.git ff
source "$HERE/features.sh"
cd ff
PKG_CONFIG_PATH="$PA/lib/pkgconfig:$PKG_CONFIG_PATH" ./configure --prefix="$W/out" $CONF --enable-static --disable-shared \
  --pkg-config-flags=--static --extra-cflags="-I$PA/include -I$P/include" --extra-ldflags="-static -L$PA/implib -L$P/lib" \
  --extra-libs="-lwhisper -lggml -lggml-base -lstdc++ -lm -lcfgmgr32 -lole32 -lshlwapi -ladvapi32"
make -j"$(nproc)"
mkdir -p "$HERE/dist/ai"
strip -o "$HERE/dist/ai/ffmpeg.exe"  ffmpeg.exe
strip -o "$HERE/dist/ai/ffprobe.exe" ffprobe.exe
# the DLLs ffmpeg.exe loads: whisper, ggml, ggml-base (at start) and the backends (ggml-vulkan, ggml-cpu-*; at run time)
for f in "$PA"/bin/*.dll; do [ "$(basename "$f")" = libparakeet.dll ] || strip -o "$HERE/dist/ai/$(basename "$f")" "$f"; done
ls -la "$HERE/dist/ai"
objdump -p "$HERE/dist/ai/ffmpeg.exe" | grep "DLL Name"
