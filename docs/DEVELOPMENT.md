# Development

## Workflow
1. Branch from `main` (`git switch -c fix/something`), change code, bump version + changelog.
2. Run the tests (`tests/README.md`), build a test zip (below), send it to the tester.
3. Push the branch, open a PR, merge. Tag `vX.Y.Z` and publish a Release with the zip.

## Building a release zip
Heavy binaries are not in git. Keep a folder (the *assets* dir) containing:
`ffmpeg.exe`, `ffprobe.exe`, `rife/`, `python/`, `ClipShrinker.exe`
(the same bundle is attached to a GitHub Release as `binaries.zip`).

    python tools/build_release.py --assets path/to/assets --out dist

Fetching the bundle (works from the Claude sandbox; `gh release download` does not, it needs GraphQL):

    ID=$(gh api repos/Vergilcd/ClipShrinker/releases/tags/binaries --jq '.assets[]|select(.name=="binaries.zip").id')
    gh api -H "Accept: application/octet-stream" repos/Vergilcd/ClipShrinker/releases/assets/$ID > binaries.zip
    unzip -q binaries.zip -d assets

Output: `dist/ClipShrinker_v<APP_VERSION>.zip` (version read from `library/video_shrinker.py`).

The launcher is built with
`GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags "-H windowsgui -s -w"` in `launcher/`.

## Portable Python
`tools/Prepare-Portable-Python.bat` (double-click; calls `Setup.bat portable`) downloads python-build-standalone (cpython 3.12,
install_only) via `get-python.ps1`, verifies tkinter and trims unused files. The result goes to `library/python`.

## Internals cheat sheet
- Timeline drawing: `ClipRow.redraw`; thumbnails: `ThumbPool`, `_sched_thumbs`, `_want_n1`.
- Encode: `Encoder` (multi-pass size targeting), filtergraph builders for xfade/zoom/speed.
- Sidebar: `_build_side`; theme: `apply_theme`, PALETTE.
- Old v0.9.8 handoff (still mostly valid): `docs/HANDOFF_old.md`.
