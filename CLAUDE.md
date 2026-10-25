# ClipShrinker - working notes for Claude

Windows 11 video trim/shrink tool. Python 3.12 + tkinter + sv_ttk, slim custom ffmpeg with NVENC. MIT.
Owner: Vergil (does not build software himself - give one-click steps, test zips, plain explanations).

## Layout
- `library/video_shrinker.py` - single-file app (~6500 lines): App, ClipRow, ThumbPool, Encoder, PopupMenu, ...
- Root `README.txt`, `CHANGELOG.txt`, `LICENSE.txt`, `ffmpeg-*.txt` sit at repo root because the About window
  reads them from the parent of `library/`. They ship in the zip root.
- `launcher/` Go launcher; `tools/build_release.py` builds the zip; `tests/` harness; `docs/DEVELOPMENT.md` details.
- Binaries NOT in git (see .gitignore): ffmpeg.exe, ffprobe.exe, library/rife/, library/python/, ClipShrinker.exe.
  They live in GitHub Releases (asset bundle `binaries.zip`) and are passed to the build script via `--assets`.

## Standing rules
- Bump the version for EVERY build, even small fixes: `APP_VERSION` in video_shrinker.py (window title derives
  from it), README.txt title, CHANGELOG.txt entry (newest first), zip name. The user picks the number.
- Every build includes an updated CHANGELOG.txt. Never ship `settings.json`.
- Mockup first when the user asks for one; do not implement before it is approved.
- Deliver a test zip in chat for each new version; the user tests on Windows (I cannot run it there).
- Branch per change (`feature/...`, `fix/...`), push, user merges. Releases/tags are published by the user.
- Commit messages: short imperative subject, body explains why.

## Decisions
- No code signing (not worth it). Deshake dropped. AV1 removed; HEVC/H.264 NVENC only.
- Portable Python = python-build-standalone (nuget/embeddable lack tkinter). Trimmed to ~36 MB.
- Post-1.0 ideas: plugin support (extra effects), sound-effect library (needs audio tracks in timeline).

## Testing in the sandbox
- Linux uv Python 3.12 + `xvfb-run -a -s "-screen 0 1600x1000x24"`; see `tests/README.md`.
- Thumbnails don't render in the sandbox (fake them). Nothing is verified on real Windows until the user tests.
- GitHub release downloads from the sandbox may be blocked; if so ask the user to attach binaries.

## Gotchas
- Theme switches reset style fonts: restore via `apply_theme` / `self._small_font`.
- Popups: global click/Configure binds close PopupMenu; single-instance windows via `_already_open`.
- Settings canvas `fit()` has re-entrancy guard + hysteresis; keep one-line labels in the sidebar.
- .bat/.ps1 must keep CRLF (.gitattributes handles it).
