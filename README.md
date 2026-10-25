# ClipShrinker

**Trim, speed up, join and shrink game clips to an exact file size - fast, on your NVIDIA GPU.**

<img width="1502" height="1202" alt="image" src="https://github.com/user-attachments/assets/cde5d0d5-6b2b-445d-9182-fa9cda4459f0" />

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

The release zip already contains a portable Python, a slim LGPL ffmpeg build and RIFE, so it runs on a PC
with nothing installed. If one of them is missing, a setup window downloads it on first start.

## Documentation

- [README.txt](README.txt) - full user guide (also shown in the app's About window)
- [CHANGELOG.txt](CHANGELOG.txt) - version history
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) - how the repo, builds and tests work

## Repository layout

| Path | Contents |
|---|---|
| `library/video_shrinker.py` | The whole application (Python / tkinter) |
| `library/sv_ttk/` | Windows 11 theme (MIT) |
| `library/Setup.bat`, `get-python.ps1` | First-run downloader for ffmpeg / portable Python |
| `launcher/` | Go source of `ClipShrinker.exe` (tiny launcher) |
| `tools/build_release.py` | Assembles the release zip |
| `tools/ffmpeg-build/` | Recipe for the slim ffmpeg build |
| `tests/` | Headless GUI/engine tests |

## License

MIT - see [LICENSE](LICENSE). ffmpeg is a slim LGPL build of the [FFmpeg](https://ffmpeg.org) project
([settings](ffmpeg-build-settings.txt), [license](ffmpeg-LICENSE.txt)). RIFE (ncnn) and sv_ttk keep their own licenses.

Made by Vergil, built together with [Claude](https://claude.com).
