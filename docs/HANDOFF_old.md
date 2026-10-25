# ClipShrinker - developer handoff (after v0.9.8)

Windows tool: Python/tkinter + sv_ttk + ffmpeg (NVENC). Open source (MIT), made by Vergil with Claude.
Master source: library/video_shrinker.py inside ClipShrinker_v0.9.8.zip (single file).

## Standing rules
- Bump version every build (window title x2, README, zip name), even for small fixes. User picks numbers; 1.0 is the target.
- Include updated CHANGELOG.txt in every build. No settings.json in the package.
- Package layout: ClipShrinker/{ClipShrinker.exe, README.txt, CHANGELOG.txt, LICENSE.txt, ffmpeg-LICENSE.txt,
  ffmpeg-build-settings.txt, library/{video_shrinker.py, ffmpeg.exe, ffprobe.exe, rife/, Setup.bat, clipshrinker.ico, sv_ttk/}}

## Decisions
- Tracked zoom (numpy) removed; zoom = 0.9.2 engine (zoom_times/zoom_spans/zoom_vfilter, tuples (a,b,x,y,w)).
- AV1 removed (GPU reported unsupported). HEVC/H.264 NVENC only.
- Slim custom ffmpeg built from kit/ (features.sh, build-slim-ffmpeg.bat). Needs muxers pcm_s16le etc. for preview audio.
- RIFE bundled (library/rife), partial-download code (RangeFile) still exists as fallback.
- Launcher (Go, GOOS=windows go build -ldflags "-H windowsgui -s -w") does not require avcodec DLLs.
- Saved library/settings.json overrides new defaults.

## Key internals
- Shortcuts: KEY_DEFAULTS, chord_from_event, App.key_conflict/set_key/_on_bound_key, _on_any_key (single <Key> binding).
- File naming: clean_suffix, _name_tag, _init_dir, _auto_path; settings "suffix", "save_dir".
- Fast pitch: FAST_PITCH list, AUD["high"], asetrate+aresample in seg_audio; settings key fast_pitch.
- Settings window built from blocks into colL / colR.

## Testing approach (sandbox)
- uv python 3.12 + xvfb-run for GUI tests; stub tk harness t/stubtk.py for non-tk python.
- Needs a Linux ffmpeg (full build for lavfi/sine tests). Not included here (500 MB).
- GUI tests need app.after queue hack for thread callbacks.

## TODO for v0.9.9
1. After deleting the active clip select the next clip, else the previous, never jump back to the first.
2. After export show "Export finished in X minutes Y seconds", then the rest of the info.
3. Top total ("N clip(s), 3:57.57 in total after trimming") ignores speed-up/slow-mo; include time scaling (check transitions too).
4. More clip transition effects (xfade has many), ideally hover previews in the dropdown (custom popup list; fallback: Preview button).

## 1.0 list
Plugin support (ask user what plugins should do), real ClipShrinker.exe packaging (fix double taskbar icon),
optional deshake/stabilization, fine tuning/bug fixes. Maybe later: A->B zoom path within one zoom section.
