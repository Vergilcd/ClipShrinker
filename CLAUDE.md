# ClipShrinker - working notes for Claude

Windows 11 video trim/shrink tool. Since 2.0.0: C# / .NET 10 WPF, slim custom ffmpeg with NVENC. MIT.
Owner: Vergil (does not build software himself - give one-click steps, test zips, plain explanations).
The Python 1.0.x app (tkinter) is in the `v1.0.8` tag; a frozen copy is `tools/reference/video_shrinker_1.0.8.py`.

## Layout
- `src/ClipShrinker.App` WPF app (Views, ViewModels, Media = preview); `src/ClipShrinker.Core` export engine,
  ffmpeg commands, projects, settings, RIFE; `src/ClipShrinker.Cli` command-line front end for tests.
- `tests/ClipShrinker.Tests` xUnit tests; `tests/fixtures` golden JSON made from 1.0.8 by `tools/dump_*.py`.
- `dist/` holds README.txt, CHANGELOG.txt, LICENSE.txt, THIRD-PARTY-NOTICES.txt and the ffmpeg texts. The release
  script copies them next to the exe, where the About window reads them (Debug builds find `dist/` directly).
- Binaries NOT in git (see .gitignore): ffmpeg.exe, ffprobe.exe, library/rife/, ClipShrinker.exe, test media.
  `tools/make_release.ps1` takes ffmpeg/ffprobe/RIFE from a `Dev\library` folder next to the repo folder.

## Standing rules
- Bump the version for EVERY build, even small fixes: `AppVersion` in src/ClipShrinker.Core/Constants.cs (window
  title and zip name derive from it), dist/README.txt title, dist/CHANGELOG.txt entry (newest first).
  The user picks the number.
- Every build includes an updated CHANGELOG.txt. Never ship `settings.json`.
- Exports must stay identical to 1.0.8 unless the user asks otherwise: C# tests must match the golden fixtures.
- Mockup first when the user asks for one; do not implement before it is approved.
- Deliver a test zip for each new version; the user tests on Windows and prefers to do audio checks by ear.
- Branch per change (`feature/...`, `fix/...`), push, user merges. Releases/tags are published by the user.
- Commit messages: short imperative subject, body explains why.

## Decisions
- No code signing (not worth it). Deshake dropped. AV1 removed; HEVC/H.264 NVENC only.
- 2.0 look deliberately differs from 1.0.8 (edge-to-edge thumbnails, keycap hotkeys, every shortcut rebindable).
- Missing ffmpeg -> in-app download of the BtbN LGPL build (FfmpegDownloader).
- Post-1.0 ideas: plugin support (extra effects), sound-effect library (needs audio tracks in timeline).

## Testing
- `dotnet test` runs the unit and fixture tests. `tools/make_test_clips.py` regenerates tests/media.
- GUI checks: `tools/gui_export_check.ps1` (`-Exe` for the packaged exe) and `tools/gui_phase6_check.ps1` (backs up
  and restores library settings/presets/session; refuses to run while the app is open).

## Gotchas
- .bat/.ps1/.txt must keep CRLF (.gitattributes handles it).
- README.md was edited by hand by the owner (screenshot, wording, feature list): edit it in place, never regenerate it.
