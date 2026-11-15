# ClipShrinker

**Trim, join and shrink game clips to an exact file size on your NVIDIA GPU.**

<img width="1386" height="1135" alt="image" src="https://github.com/user-attachments/assets/dfc2c288-6197-4f85-b6c2-cac43505097f" />

ClipShrinker is a simple modular Windows tool for turning big gameplay recordings into clips that fit Discord or other services.

Set a size limit (eg. 50 MB), trim on a thumbnail timeline, join multiple clips, then export. The app re-encodes with bitrate iteration until the file size lands just under the limit.

## Features
- **Size-targeted export** - MegaByte limit can be set to fit under share size limits
- **Bitrate export** - Set a bitrate by pre-saved presets or apply one to all clips
- **Thumbnail timeline** for each clip with draggable trim handles or typed-in timestamps
- **Join clips** with transitions (crossfade, wipes, swipes or a glitch effect) or export separately
- **Centered crop** to 21:9, 16:9, 16:10 or even 4:3 for cutting ultrawide gameplay
- **Multiple audio tracks** - keep game and microphone separate or mix them on one track
- **HEVC / H.264 via NVENC**, HDR clips handled or can be converted to SDR output
- **Live preview** with sound, crop, and effects applied
- **Picture adjustments** with simple denoiser, brightness, contrast and saturation sliders
- **Dark and light theme** (Windows Fluent style)
- **Project saving & loading** with optional session saving at exit
- **Portable:** no installation needed, everything is bundled

### 3 built in timeline effects as plugin
- **Speed sections** (fast-forward with custom multipliers)
- **Slow motion sections** (with RIFE frame interpolation available)
- **Zoom sections** to focus on details

Drag/click on the buttons or use the shortcuts to apply them to the timeline

## Modular build with plugins
<img width="646" height="627" alt="image" src="https://github.com/user-attachments/assets/bc882e4c-cb0e-410d-8825-fab6ed959ef4" />

- Core features include cutting and joining clips with file size shrinking
- Rest of the features are broken into plugins that can be switched off or even deleted to avoid clutter if not needed
- Additional features can be easily added without touching core functions.

## Customizable shortcuts
<img width="1131" height="745" alt="image" src="https://github.com/user-attachments/assets/e492440b-0bbb-45db-b6b4-7890617f3131" />

## Requirements

- Windows 10/11 (64-bit)
- NVIDIA GPU with NVENC (GTX 10xx or newer) and a recent driver
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
which is Apache 2.0 ([license](dist/OpenCL-ICD-Loader-LICENSE.txt)). RIFE (ncnn) keeps its own license.

Made by Vergil, built together with [Claude](https://claude.com).
