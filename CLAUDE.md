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
- `modules/<id>/` (2.4): module.json + CHANGELOG.txt of the built-in modules (timeline-effects, bitrate-presets,
  picture-adjust, sound-equalizer, sound-track). Embedded in the exe (seeded on first start, offline fallback for Restore), copied into the
  release, and fetched from raw.githubusercontent .../main/modules by Restore. Their code is still in the app;
  `Core.Modules.Features` / `AppState.PictureOn` gate it. RIFE lives in modules\timeline-effects\rife.
  2.7 renamed "plugins" to "modules" everywhere (the owner's call: they are built in, not add-ons).
  AppPaths.MoveOldModulesFolder moves an old plugins\ folder over; settings "plugins"/"plugins_known" are still
  read. The repo's old `plugins/` folder stays (frozen) because 2.4-2.6 Restore downloads from main/plugins.
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
- No code signing (not worth it). Deshake dropped. AV1 removed; HEVC / H.264 only.
- 2.0 look deliberately differs from 1.0.8 (edge-to-edge thumbnails, keycap hotkeys, every shortcut rebindable).
- Missing ffmpeg -> in-app download of the BtbN LGPL build (FfmpegDownloader).
- Post-1.0 ideas: module support (extra effects), sound-effect library (needs audio tracks in timeline).
- 2.5.0 denoise: nlmeans_opencl (GPU, static Khronos OpenCL loader in ffmpeg) with CPU nlmeans fallback; probed at
  start-up (Tools.OpenClWorks). 8-bit only, so clips that stay HDR are not denoised. hqdn3d is GPL-only; atadenoise
  smeared moving footage. Slider 0-100 % maps to strength 1-5.5 (the first test showed more only blurs).
- 2.5.0: clip transitions are an app feature (Transition drop-down next to "Clear all clips", shown with 2+ clips),
  not part of the Timeline effects module. Built-in module files are rewritten when the embedded version is newer.
- 2.6.0: encoders for every PC (Core/Media/Encoders.cs): NVENC, AMF, QSV, Media Foundation on GPU (_mf_hw key) and
  CPU. Tools.WorkingEncoders test-encodes each at start-up; the Codec list shows only those; Encoders.Fallback keeps
  HEVC/H.264. NVENC args unchanged (golden). x264/x265 are GPL - not allowed. AMD/Intel untested (only an RTX 5070
  here, Ryzen 5900X has no iGPU): set CLIPSHRINKER_HIDE_ENCODERS=nvenc,cuda,opencl to act like a non-NVIDIA PC.
- 2.6.0 Sound equalizer module (sound-equalizer): 5 bands + volume in dB, one audio filter after mixing/joining on
  every output track (GraphBuilder.Build wraps BuildRaw) and on the preview sound. Neutral = no filter.
- 2.7.0 Sound track module (sound-track): TimelineClip.Sounds (SoundBlock: At in clip source time, In/Len real
  seconds). Export: each sound file is an extra ffmpeg input after clips + RIFE frames, mixed into the clip's
  first audio track before joining (GraphBuilder, Sounds.MixParts, amix + alimiter). UI: Views/SoundLaneView
  (own control under TimelineView, shares Editor.X/T), Views/SoundsPanel drop-down, key "sound" = A. Projects
  write "sounds" only when a clip has any. tools/gui_sounds_check.ps1 checks it (ask first: it takes focus).
  The Sounds list items are green draggable chips (waveform, play, +); no Sound chip among the effect buttons.
- make_release.ps1 keeps the owner's files in Release\ClipShrinker (modules\sound-track\sounds = the owner's example
  sounds, library\*.json) across rebuilds but leaves them out of the zip.

## Testing
- `dotnet test` runs the unit and fixture tests. `tools/make_test_clips.py` regenerates tests/media.
- GUI checks: `tools/gui_export_check.ps1` (`-Exe` for the packaged exe) and `tools/gui_phase6_check.ps1` (backs up
  and restores library settings/presets/session; refuses to run while the app is open). `tools/gui_modules_check.ps1`
  checks the Modules window, switching / deleting / restoring and saves screenshots to %TEMP%\cs_modules_shots.
- Tests that flip `Features` go in the "ModuleFlags" xUnit collection (runs alone).

## Gotchas
- .bat/.ps1/.txt must keep CRLF (.gitattributes handles it).
- README.md was edited by hand by the owner (screenshot, wording, feature list): edit it in place, never regenerate it.
