# ClipShrinker

**Trim, join and shrink game clips to an exact file size on your NVIDIA GPU.**

<img width="1386" height="1020" alt="image" src="https://github.com/user-attachments/assets/30f315c6-4c4a-4c57-9904-7c426cfb7e80" />

ClipShrinker is a small Windows tool for turning big gameplay recordings into clips that fit
Discord. Set a size limit (eg. 50 MB), trim on a thumbnail timeline, then encode with NVENC, 
the app checks the result and re-encodes with a corrected bitrate until the file lands just under the limit.

## Features
- **Size-targeted export** - MegaByte limit can be set to fit under share size limits
- **Bitrate export** - Set a bitrate by pre-saved preset or apply one to all clips
- **Thumbnail timelines** per clip with draggable trim handles or typed-in timestamps
- **Join clips** with transitions (crossfade, wipes, swipes or a glitch effect) or export separately
- **Centered crop** to 21:9, 16:9, 16:10 or 4:3 for trimming superwide clips
- **Multiple audio tracks** - keep game and microphone separate or mix them
- **HEVC / H.264 via NVENC**, HDR clips handled or can be converted to SDR output
- **Live preview** with sound, crop, and effects applied
- **Dark and light theme** (Windows Fluent style),
- **Project saving & loading** optional session saving at exit available
- **Portable:** no installation needed, everything is bundled

### 3 built in effects
- **Speed sections** (fast-forward with custom multipliers)
- **Slow motion sections** (with RIFE frame interpolation available)
- **Zoom sections** to focus on details

Drag/click on the buttons or use the shortcuts to apply them to the timeline

## Customizable shortcuts
<img width="1131" height="745" alt="image" src="https://github.com/user-attachments/assets/d2852dad-2c34-4fbb-b340-f819da3d4a7e" />

## Requirements

- Windows 10/11 (64-bit)
- NVIDIA GPU with NVENC (GTX 10xx or newer) and a current driver

## Install

1. Download the latest `ClipShrinker_vX.Y.Z.zip` from the [Releases](../../releases) page.
2. Unzip anywhere and double-click `ClipShrinker.exe`. Keep the `library` folder next to the exe.

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
([settings](dist/ffmpeg-build-settings.txt), [license](dist/ffmpeg-LICENSE.txt)). RIFE (ncnn) keeps its own license.

Made by Vergil, built together with [Claude](https://claude.com).
