# ClipShrinker

**Trim, join and shrink game clips to an exact file size on your GPU.**

<img width="1386" height="1164" alt="image" src="https://github.com/user-attachments/assets/5cbddbb9-2724-479a-ae7b-02c64e619265" />

ClipShrinker is a simple modular Windows tool for turning big gameplay recordings into clips that fit Discord or other services.

Set a size limit (eg. 50 MB), trim on a thumbnail timeline, join multiple clips, then export. The app re-encodes with bitrate iteration until the file size lands just under the limit.

## Core features
- **Size-targeted export** - MB limit can be set to fit under upload size limits
- **Thumbnail timeline** for each clip with draggable trim handles or typed-in timestamps
- **Join clips** with transitions (crossfade, wipes, swipes or a glitch effect) or export separately
- **Centered crop** to 21:9, 16:9, 16:10 or even 4:3 for cutting ultrawide gameplay
- **Multiple audio tracks** - keep game and microphone separate or mix them on one track
- **HEVC / H.264 on NVIDIA, AMD or Intel graphics** (or Windows' own encoder as a fallback); HDR clips handled or converted to SDR
- **Live preview** with sound, crop, and effects applied
- **Dark and light theme** (Windows Fluent style)
- **Project saving & loading** with optional session saving at exit
- **Portable:** no installation needed, everything is bundled

## Additional features with removable modules
- **Bitrate export** - Set a bitrate by pre-saved presets or apply one to all clips
- **Picture adjustments** with simple denoiser, brightness, contrast and saturation sliders
- **Sound equalizer** with five bands and an overall volume slider in dB
- **Speed Up effect** adjustable length on timeline (fast-forward with custom multipliers)
- **Slow motion effect** adjustable length on timeline (with RIFE frame interpolation available)
- **Zoom effect** adjustable level and placement on preview and length on timeline
- Add **sounds** to the timeline from explorer or by dragging from your collection (kept in the /sound folder)

## Modular build
<img width="646" height="813" alt="image" src="https://github.com/user-attachments/assets/c9da9ffa-8df6-49dd-bf1f-3ded4073f052" />

Switch off or even delete modules that you do not need (all plugins are on by default)

## Customizable shortcuts
<img width="1196" height="940" alt="image" src="https://github.com/user-attachments/assets/2e2b37cc-0fb7-489f-bf9b-29c99fa7477c" />

## Requirements

- Windows 10/11 (64-bit)
- A graphics card with a recent driver is recommended: NVIDIA (NVENC, GTX 10xx or newer), AMD (AMF) or Intel (Quick Sync). Without one, Windows' own encoder is used, on the CPU as a last resort (slower, H.264 only). The "GPU Encoding is ON/OFF" notice in the top bar shows what your PC uses.
- The Picture adjustments plugin includes a denoiser that uses OpenCL. This also requires GPU with a recent driver, otherwise it falls back to using CPU which is way slower.

## Install

1. Download the latest `ClipShrinker-X.Y.Z.zip` from the [Releases](../../releases) page.
2. Unzip anywhere and double-click `ClipShrinker.exe`. Keep the `library` and `plugins` folder next to the exe.

Windows may show a SmartScreen warning because the exe is not code-signed: **More info -> Run anyway**.

The release zip contains one self-contained `ClipShrinker.exe` (.NET built in), a slim LGPL ffmpeg build and
RIFE, so it runs on a PC with nothing installed. If ffmpeg is missing, the program offers to download it once.

## Documentation

- [README.txt](dist/README.txt) - full user guide (also shown in the app's About window)
- [CHANGELOG.txt](dist/CHANGELOG.txt) - version history
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) - how the repo, builds and tests work

## Repository layout

Since 2.0.0 ClipShrinker is a C# / .NET 10 WPF app. The Python 1.0.x version is in the
[v1.0.8](../../tree/v1.0.8) tag.

| Path | Contents |
|---|---|
| `src/ClipShrinker.App/` | The WPF application (windows, view models, preview) |
| `src/ClipShrinker.Core/` | Export engine, ffmpeg commands, projects and settings |
| `src/ClipShrinker.Cli/` | Command-line front end used by the tests |
| `tests/` | xUnit tests and the golden fixtures from 1.0.8 |
| `tools/make_release.ps1` | Builds the exe and the release zip |
| `tools/reference/`, `tools/dump_*.py` | Frozen 1.0.8 source and the scripts that make the fixtures |
| `tools/ffmpeg-build/` | Recipe for the slim ffmpeg build |
| `dist/` | The text files shipped in the zip (README, CHANGELOG, licenses) |

## License

MIT - see [LICENSE](LICENSE) (third-party parts: [THIRD-PARTY-NOTICES.txt](dist/THIRD-PARTY-NOTICES.txt)). ffmpeg is a slim LGPL build of the [FFmpeg](https://ffmpeg.org) project
([settings](dist/ffmpeg-build-settings.txt), [license](dist/ffmpeg-LICENSE.txt)). It includes the Khronos OpenCL loader for the GPU denoiser,
which is Apache 2.0 ([license](dist/OpenCL-ICD-Loader-LICENSE.txt)). It also includes AMD's AMF headers ([license](dist/AMF-LICENSE.txt)) and Intel's oneVPL dispatcher ([license](dist/oneVPL-LICENSE.txt)), both MIT.
RIFE (ncnn) keeps its own license.

Made by Vergil, built together with [Claude](https://claude.com).
