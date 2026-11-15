# Development

ClipShrinker 2.x is a C# / .NET 10 WPF app (`ClipShrinker.sln`). The Python 1.0.x version is in the `v1.0.8` tag.

## Workflow
1. Branch from `main` (`git switch -c fix/something`), change code, bump `AppVersion` in
   `src/ClipShrinker.Core/Constants.cs` and add a `dist/CHANGELOG.txt` entry.
2. Run the tests (below), build a release zip, send it to the tester.
3. Push the branch, open a PR, merge. Tag `vX.Y.Z` and publish a Release with the zip.

## Tests
    dotnet test

The tests compare the C# export commands, filters, projects and timeline maths with golden fixtures in
`tests/fixtures`, generated from the frozen 1.0.8 source (`tools/reference/`) by `tools/dump_*.py`.
`tools/make_test_clips.py` creates the sample clips in `tests/media` (not in git), and
`tools/compare_exports.py` / `tools/reference_export.py` compare real exports with 1.0.8.
`tools/gui_export_check.ps1` and `tools/gui_phase6_check.ps1` drive the running app.

## Building a release zip
Heavy binaries are not in git. The script expects a `Dev\library` folder next to the repo folder with
`ffmpeg.exe`, `ffprobe.exe` and `rife\` (rife-ncnn-vulkan.exe, vcomp140.dll, LICENSE.txt, rife-v4.6\).
The Release tag `binaries` on GitHub holds these in `binaries.zip`.

    powershell -ExecutionPolicy Bypass -File tools\make_release.ps1

It publishes one self-contained `ClipShrinker.exe` (win-x64), copies the `dist\` texts, ffmpeg / ffprobe into
`library\`, the repo's `plugins\` folder with RIFE in `plugins\timeline-effects\rife\` into
`..\Release\ClipShrinker\` and zips them as `..\Release\ClipShrinker-<version>.zip`.

## Plugins
Each folder in `plugins\` is one plugin: `plugin.json` (id, name, author, version, builtin, description) and
`CHANGELOG.txt`. Bump a plugin's version and changelog when its part of the app changes. The Plugins window's
Restore downloads these two files from `main` on GitHub, so a changed plugin.json must be merged to reach users.

The slim ffmpeg build recipe is in `tools/ffmpeg-build/`.
