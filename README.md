# ClipShrinker

**Trim, join and shrink game clips to an exact file size on your GPU.**

<img width="1386" height="1164" alt="image" src="https://github.com/user-attachments/assets/8607424e-20b6-43e3-a51b-95d5a9ba316a" />

ClipShrinker is a modular Windows tool for turning big gameplay recordings into clips that fit Discord or other services, but it can do much more.

Set a size limit (eg. 50 MB), trim on a thumbnail timeline, join multiple clips, then export. The app re-encodes with bitrate iteration until the file size lands just under the limit.

Optionally, [Claude](https://claude.com/claude-code) can do the editing for you: describe the moment you want, or let it suggest, then it trims, adds effects and sounds, or even subtitle files (see [AI editing](#ai-editing-optional)).

## Core features
- **Size-targeted export**, **Thumbnail timeline**, **Join multiple clips with transitions**
- **Multi audio track support** for keeping game and mic tracks separate or mixed
- **Centered crop** to 21:9, 16:9, 16:10 or even 4:3 for cutting ultrawide gameplay
- **HEVC / H.264 on NVIDIA, AMD or Intel graphics** (or Windows' own encoder as a fallback); HDR clips handled or converted to SDR
- **Dark and light theme** (Windows Fluent style)
- **Project saving & loading** with optional session saving at exit
- **Portable:** no installation needed, everything is bundled

## Additional features with removable modules
- **Bitrate export** - Set a bitrate by pre-saved presets or apply one to all clips
- **Picture adjustments** with simple denoiser, brightness, contrast, saturation and white balance (temperature, tint) sliders
- **Sound equalizer** with five bands and an overall volume slider in dB
- **Speed Up effect** adjustable length on timeline (fast-forward with custom multipliers)
- **Slow motion effect** adjustable length on timeline (with RIFE frame interpolation available)
- **Pause effect** freezes one frame for a set length (eg. 1.5 s) while your added sounds keep playing, then the clip carries on. Good for a freeze on an impact
- **Zoom effect** adjustable level and placement on preview and length on timeline
- Add **Sounds** to the timeline from explorer or by dragging from your collection (kept in the `modules\sound-track\sounds` folder)
- **AI editing** with Claude Code (optional download, see below)

## Modular build
<img width="646" height="813" alt="image" src="https://github.com/user-attachments/assets/3bc7d927-aaf0-40f6-9dce-de4bfa328dd1" />

Switch off or even delete modules that you do not need. All modules are on by default, except AI editing, which is installed and switched on separately.

## Customizable shortcuts
<img width="1196" height="980" alt="image" src="https://github.com/user-attachments/assets/48f3ac10-a088-46e9-b011-7d6fb6a53933" />

## Agentic editing (optional)

Connect ClipShrinker to [Claude Code](https://claude.com/claude-code) and edit by describing what you want, eg. *"trim to the action, slow down the first bullet exchange and add a satisfying sound effect when the fight is won"*. Claude looks at frames of the clip, then works on the clips open in the app. Every change shows on the timeline right away and can be undone with Ctrl+Z. It can also work without the app open and just batch trim multiple clips based on the prompt.

Claude can use all tools and modules:
- find moments by what happens on screen, by loud peaks (shots, explosions) or by what is said
- trim clips and add speed, pause and zoom effects
- add sound effects from your collection, or download one from a free sound site
- change the picture and export settings, and export when you ask
- write subtitles (.srt) from speech and context, do translations on them etc...

Set it up in **Modules > AI editing > Install** (about 60 MB, plus 550 MB for speech to text), then click **Connect to Claude Code** and start a new Claude Code session. Speech to text runs on the graphics card (Vulkan) when there is one.

Disclaimer:
AI editing sends frames, speech, file names and settings of the clips Claude works on to Anthropic. It stays off until you switch it on.

## Requirements

- Windows 10/11 (64-bit)
- A graphics card with a recent driver is recommended: NVIDIA (NVENC, GTX 10xx or newer), AMD (AMF) or Intel (Quick Sync). Without one, Windows' own encoder is used, on the CPU as a last resort (slower, H.264 only). The "GPU Encoding is ON/OFF" notice in the top bar shows what your PC uses.
- The Picture adjustments module includes a denoiser that uses OpenCL. This also requires GPU with a recent driver, otherwise it falls back to using CPU which is way slower.
- AI editing needs [Claude Code](https://claude.com/claude-code) and a Claude subscription or API account. Speech to text is much faster on a graphics card with Vulkan support (most NVIDIA, AMD and Intel cards); without one it runs on the CPU.

## Install

1. Download the latest `ClipShrinker-X.Y.Z.zip` from the [Releases](../../releases) page.
2. Unzip anywhere and double-click `ClipShrinker.exe`. Keep the `library` and `modules` folders next to the exe.

To update, unzip the new version over the old folder. Your settings, modules and sounds are kept.

Windows may show a SmartScreen warning because the exe is not code-signed: **More info -> Run anyway**.

The release zip contains one self-contained `ClipShrinker.exe` (.NET built in), a slim LGPL ffmpeg build and
RIFE, so it runs on a PC with nothing installed. If ffmpeg is missing, the program offers to download it once.
AI editing is not in the zip: the app downloads it from the [ai-editing](../../releases/tag/ai-editing) release when you install it.

## Documentation

- [README.txt](dist/README.txt) - full user guide (also shown in the app's About window)
- [CHANGELOG.txt](dist/CHANGELOG.txt) - version history
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) - how the repo, builds and tests work
- [docs/mcp-agent-research.md](docs/mcp-agent-research.md) - how AI editing was designed

## Repository layout

Since 2.0.0 ClipShrinker is a C# / .NET 10 WPF app. The Python 1.0.x version is in the
[v1.0.8](../../tree/v1.0.8) tag.

| Path | Contents |
|---|---|
| `src/ClipShrinker.App/` | The WPF application (windows, view models, preview) |
| `src/ClipShrinker.Core/` | Export engine, ffmpeg commands, projects, settings and the AI editing tools |
| `src/ClipShrinker.Cli/` | Command-line front end used by the tests, and the AI editing helper (`clipshrinker mcp`) |
| `modules/` | Version and changelog of each built-in module |
| `tests/` | xUnit tests and the golden fixtures from 1.0.8 |
| `tools/make_release.ps1` | Builds the exe and the release zip |
| `tools/make_ai_module.ps1` | Builds the AI editing download (helper, speech ffmpeg, models) |
| `tools/reference/`, `tools/dump_*.py` | Frozen 1.0.8 source and the scripts that make the fixtures |
| `tools/ffmpeg-build/` | Recipe for the slim ffmpeg build |
| `dist/` | The text files shipped in the zip (README, CHANGELOG, licenses) |

## License

MIT - see [LICENSE](LICENSE) (third-party parts: [THIRD-PARTY-NOTICES.txt](dist/THIRD-PARTY-NOTICES.txt)). ffmpeg is a slim LGPL build of the [FFmpeg](https://ffmpeg.org) project
([settings](dist/ffmpeg-build-settings.txt), [license](dist/ffmpeg-LICENSE.txt)). It includes the Khronos OpenCL loader for the GPU denoiser,
which is Apache 2.0 ([license](dist/OpenCL-ICD-Loader-LICENSE.txt)). It also includes AMD's AMF headers ([license](dist/AMF-LICENSE.txt)) and Intel's oneVPL dispatcher ([license](dist/oneVPL-LICENSE.txt)), both MIT.
RIFE (ncnn) keeps its own license.
The AI editing download adds an ffmpeg build with [whisper.cpp](https://github.com/ggml-org/whisper.cpp) (MIT, license included in the download), the Whisper large-v3-turbo speech model (MIT, OpenAI) and the Silero VAD model (MIT).

Made by Vergil, built together with [Claude](https://claude.com).
