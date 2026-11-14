# ClipShrinker

**Trim, speed up, join and shrink game clips to an exact file size - fast, on your NVIDIA GPU.**

<img width="1502" height="1005" alt="image" src="https://github.com/user-attachments/assets/a223f8d1-245a-4072-9105-561aa27185d8" />

ClipShrinker is a small Windows 11 tool for turning big Shadowplay recordings into clips that fit
Discord, forums or chat apps. Set a size limit (eg. 50 MB), trim on a thumbnail timeline, and it encodes
with NVENC, checks the result and re-encodes with a corrected bitrate until the file lands just under the limit.

## Features

- **Size-targeted export** - give it a MB limit (or a fixed bitrate) and it iterates until the file fits the limit
- **Thumbnail timelines** per clip with draggable trim handles and typed-in timestamps
- **Speed sections** (fast-forward with custom multipliers)
- **Slow motion sections** (with RIFE frame interpolation available)
- **Zoom sections** to punch in on the action
- **Join clips** with transitions (crossfade, wipes, swipes or a glitch effect) or export separately
- **Centered crop** to 21:9, 16:9, 16:10 or 4:3 for trimming superwide records
- **Multiple audio tracks** - keep game and microphone separate or mix them
- **HEVC / H.264 via NVENC**, HDR clips handled
- **Live preview** with sound, crop, speed and zoom applied
- **Dark and light theme** (Windows 11 style), session saving, project saving/loading
- **Customizable shortcuts** for main features
- **Portable:** nothing is installed on your PC

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
