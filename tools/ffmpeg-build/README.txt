SLIM FFMPEG FOR CLIP SHRINKER (test kit)
========================================
The ffmpeg that ships with Clip Shrinker (BtbN shared build) is ~160 MB because it contains hundreds of codecs,
filters and libraries the program never uses. This kit builds an ffmpeg that has ONLY what Clip Shrinker uses
(see features.sh). Tested on Linux with the full Clip Shrinker test suite (export, speed, slow-mo, zoom, tracked
zoom, HDR tone-map, audio, preview): the slim build is ~15 MB per exe and passed all of it. The Windows build
itself is NOT tested yet - that is what this kit is for.

How to build (no programming needed, about 30-60 minutes, Windows 11):
 1. Unzip this folder anywhere (a short path such as C:\ffbuild is best).
 2. Double-click build-slim-ffmpeg.bat. It installs MSYS2 (the free build tools) through winget, then compiles.
    If the window stops early after updating MSYS2, just double-click the file again.
 3. When it says Done, the new ffmpeg.exe and ffprobe.exe are in the "dist" folder.
 (Alternative: upload this folder to a GitHub repository and run the "Build slim ffmpeg" workflow under Actions.)

Test: make a copy of Clip Shrinker's library folder first. In the library folder replace ffmpeg.exe and
ffprobe.exe with the new ones and delete the DLL files named avcodec-*, avdevice-*, avfilter-*, avformat-*,
avutil-*, swresample-*, swscale-*. Try: export H.264 and HEVC, a clip with a speed section, a slow-mo section,
a zoom section, an HDR clip with "Convert HDR", the preview with sound, and a clip from each recorder you use.
If something fails, library\clipshrinker.log names the missing piece; send me the line (or add the name to
features.sh) and rebuild.

AV1: the slim build includes the NVIDIA AV1 encoder (av1_nvenc, RTX 40 series) and the dav1d software decoder.
Known gaps in the slim build (the full build has them):
 - GPU preview scaling (scale_cuda) - the preview falls back to a slightly slower path automatically
 - decoders for rare formats (see DECODERS in features.sh)
