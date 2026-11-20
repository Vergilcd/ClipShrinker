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

- 2.8.0-dev AI editing (MCP), branch feature/mcp-dev, not released: `clipshrinker mcp` (Cli/Mcp.cs) is a stdio MCP
  server Claude Code starts. Tools live in Core/Agent/AgentTools.cs and run on an IAgentHost: the open app
  (App/ViewModels/MainViewModel.Agent.cs, AppAgentHost: UI thread, one undo step per edit) when the helper is
  connected over the named pipe "ClipShrinker.Agent" (Core/Agent/AgentPipe.cs, CurrentUserOnly), otherwise a
  .clipproj (FileHost in Mcp.cs). Module ai-agent shows the header pill (blue connected / orange not).
  Core/Analysis/ClipAnalysis.cs: contact sheets (own PNG writer + digit font), moments (PCM loudness + motion
  frames, decoded with -hwaccel cuda in 4 parts; PS5 VP9 ignores -skip_frame/-discard nokey), picture stats. The
  bundled ffmpeg has no scdet/ebur128/signalstats/tile/drawtext. Design + plan: docs/mcp-agent-research.md.
  Builds: Release\ClipShrinker-2.8.0-dev (app), Release\ClipShrinker-mcp-dev (helper registered in the owner's
  Claude Code as "clipshrinker"), Release\ClipShrinker-2.8.0-dev-ai.zip (both + Connect/Disconnect .bat + guide,
  for testers). Owner's workflow: give paths / a project, or edit clips already on the timeline live; export
  only when asked.
- 2.8.0-dev2 (owner's choices): ai-agent is OFF by default (ModuleIds.DefaultOn) and switching it on shows
  ModuleIds.AiAgentNotice (third-party data notice, every time it is switched on). New tools in
  Core/Agent/AgentTools.More.cs: export_settings / set_export_settings (IAgentHost.Settings = no undo step, the app
  undo covers clips only; the reply carries the old values), sound_library / download_sound (any direct https link
  Claude finds; Core/Agent/Downloads.cs, 40 MB cap, sources.txt in the Sounds folder) / add_sound / remove_sounds,
  transcribe (Core/Analysis/Speech.cs: ffmpeg's whisper filter, model ggml-large-v3-turbo-q5_0 + Silero VAD
  downloaded in the background into modules\ai-agent\models; ffmpeg runs in that folder so filter paths have no
  "C:"; SRT output because the filter's JSON does not escape quotes). White balance = PicTemp / PicTint
  (pic_temp / pic_tint) as U/V shifts in the same lutyuv; neutral still gives no filter.
  ffmpeg + whisper.cpp: build-ffmpeg-min.sh, static, CPU only (a Vulkan / CUDA ggml backend would make ffmpeg.exe
  need vulkan-1.dll / CUDA DLLs to start). PROCESSOR_ARCHITECTURE must be set or ggml builds its generic (no AVX)
  code, ~10x slower. The helper finds the app folder with --app (FileHost.AppDir).
  AgentTests.PipeCarriesCallsAndCountsConnections fails while a Claude session with the clipshrinker helper runs
  (the helper connects to the test's pipe too).
- 2.8.0-dev3 (owner's call): AI editing is NOT in the base package; it is installed on demand (Core/Modules/
  AiModule.cs, App/ViewModels/MainViewModel.AiInstall.cs, buttons on its Modules card: Install / Update / bin =
  Uninstall / Connect to Claude Code). Parts come from the rolling GitHub prerelease tag "ai-editing"
  (ai-editing.json lists each part with size + SHA-256; helper zip, speech-ffmpeg zip, model + VAD as optional
  files with Hugging Face fallbacks) into modules\ai-agent\{helper,ffmpeg,models} + installed.json. No ffmpeg
  swap: library\ffmpeg stays the base build (Dev\library = the 2026-11-15 build, no whisper); the whisper build
  lives in Dev\library-ai and is used only by transcribe (AiModule.SpeechFfmpeg). Installed = installed.json +
  helper exe. A running helper is renamed to *.old on update/uninstall (CleanOld at start-up). Connect runs
  `claude mcp add clipshrinker --scope user -- <helper> mcp --app <dir> --tools <dir>\library`.
  CLIPSHRINKER_AI_SOURCE = folder/URL with ai-editing.json (tests). make_release.ps1 refuses a whisper ffmpeg and
  puts pre-release versions in Release\ClipShrinker-<version> (-Out to override); tools/make_ai_module.ps1 makes
  Release\ai-editing\ and prints the gh upload commands (the owner publishes). tools/gui_ai_install_check.ps1
  installs from those files into a %TEMP% copy of the package, runs transcribe through the installed helper and
  uninstalls (no focus needed; MessageBoxes are answered with WM_COMMAND).
- 2.8.0-beta2 / AI editing 0.5.0: speech on the GPU. The module ffmpeg (Dev\library-ai; the CPU-only one is kept in
  Dev\library-ai-cpu-2026-11-19) links libwhisper.dll + ggml.dll; ggml loads ggml-vulkan.dll and the best
  ggml-cpu-*.dll at run time from ffmpeg.exe's folder (GGML_BACKEND_DL, GGML_CPU_ALL_VARIANTS; build script notes
  the -static / --exclude-libs link flags). No vulkan-1.dll or no Vulkan device = CPU by itself. Speech.Transcribe
  asks use_gpu=1; a failed GPU run is repeated on the CPU and Speech.GpuFailed keeps the CPU for the session
  (CLIPSHRINKER_SPEECH_CPU=1 starts on the CPU). vad_min_silence_duration=2: ffmpeg's filter runs whisper once per
  VAD piece (each a full 30 s window), so the default 0.5 s made ~50 runs per 160 s. 9:00-11:40 of the GTA VI test
  clip: CPU 300 s -> 200 s (2 s silence), GPU (RTX 5070) 34 s.
- 2.8.0-beta3 / AI editing 0.6.0: tool "guide" serves how-to guides embedded from src/ClipShrinker.Core/Agent/
  Guides/*.md (resource "guides/<name>.md"; a new .md there is a new topic). The helper answers "guide" itself
  (Mcp.CallTool) so an older app works too. Guide "subtitles" = the owner's hand-tuned SRT workflow (GTA VI test
  clip): whisper starts a line after a pause where the previous one ended (end is right); the game's burned-in
  subtitles are the reference for words and start times. The filter's max_len (word timing) helped but is not
  used by transcribe yet (with vad_min_silence_duration 0.5 it took 536 s for 15 min on the GPU).

- 2.9.0 Pause section (owner's design, "Insert"): a SpeedSection with Speed 0 (IsPause), orange, key P, shares the
  speed lane. The frame at A stays for B - A seconds (bar width = pause length), then the clip goes on from A: nothing
  is cut. Timing.BuildPieces gives a hold Piece(A, A, 1, Hold); Duration / SrcToOut / OutToSrc count Hold (a time at
  the pause maps to its start). Export / preview: Filters.SegVideo holds the frame with trim+fps+loop (the slim ffmpeg
  has no tpad), SegAudio puts silence (owner: game sound silent, Sound track sounds play on). Saved as speed 0; older
  versions load it as 0.1x. Sounds: TimelineClip.SoundEnd / Sounds.SrcEnd give a sound's end in source time through
  speed sections and pauses (SoundBlock.End = At + Len is only right at 1x); SoundLaneView draws and drags with it.

## Testing
- `dotnet test` runs the unit and fixture tests. `tools/make_test_clips.py` regenerates tests/media.
- GUI checks: `tools/gui_export_check.ps1` (`-Exe` for the packaged exe) and `tools/gui_phase6_check.ps1` (backs up
  and restores library settings/presets/session; refuses to run while the app is open). `tools/gui_modules_check.ps1`
  checks the Modules window, switching / deleting / restoring and saves screenshots to %TEMP%\cs_modules_shots.
- Tests that flip `Features` go in the "ModuleFlags" xUnit collection (runs alone).

## Gotchas
- .bat/.ps1/.txt must keep CRLF (.gitattributes handles it).
- README.md was edited by hand by the owner (screenshot, wording, feature list): edit it in place, never regenerate it.
