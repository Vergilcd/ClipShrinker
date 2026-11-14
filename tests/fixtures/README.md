# Golden fixtures (Phase 0)

Results from the Python 1.0.8 logic. The C# port must reproduce every one of them exactly.

- **Source:** `tools/reference/video_shrinker_1.0.8.py`, a frozen copy of `Dev/library/video_shrinker.py`
  (SHA-256 in `meta.json`). Never point the generator at the live script.
- **Regenerate** (from the `CSharp` folder):
  `..\Dev\library\python\python.exe -I tools\dump_fixtures.py`
  The output is deterministic: two runs give byte-identical files.
- Nothing runs ffmpeg. Commands are captured, and the exports use a fake file system (see `exports.json`).
- Tool paths are normalised to `ffmpeg` / `ffprobe`. RIFE counts as not installed.

## Files

| File | What it holds | Cases |
|---|---|---|
| `meta.json` | Reference hash, Python version, all constants (aspects, transitions, labels, key names...), default effect globals | - |
| `helpers.json` | Small pure functions: time formatting/parsing, `parse_fps`, `even`, `crop_size`, `choose_scale`, `video_codec_args`, `atempo_chain`, label to key mappings, `chord_label`, `clean_suffix`, `out_tracks`, `_color_tags`, `clip_vfilters`, plus `fraction_limit_denominator_1001` and `python_format` | 402 |
| `pieces.json` | `build_pieces`, `pieces_duration`, `is_plain`, `out_to_src` / `src_to_out` sampled at many points, `zoom_times`, `zoom_spans` | 65 |
| `filters.json` | `glitch_parts`, `vhs_video_parts`, `vhs_audio_parts`, `slow_audio_parts`, `zoom_vfilter`, `zoom_whoosh_parts`, `seg_video` (with and without RIFE inputs), `seg_audio` | 271 |
| `graphs.json` | `build_graph` for 40 export setups: crops, padding, speed sections, every sound mode, VHS, RIFE inputs, zooms, 1-4 audio tracks, mixing, HDR on/off, hard cut / xfade / glitch joins | 40 |
| `preview.json` | `play_command`, `preview_audio_cmd`, `effect_sample_cmd`, `grab_frame`, `grab_frames` (batch + fallback commands), and the transition hover preview for every transition | 338 |
| `exports.json` | Full `Encoder.encode` runs: every ffmpeg command of every pass, status texts, progress values, final size or error | 23 |
| `projects.json` | Project loading and the app's settings rules (`tools/dump_project_fixtures.py`, Phase 2) | 3 projects |
| `timeline.json` | The 1.0.8 `ClipRow` editing methods (`on_down`, `on_move`, `add_section`, `set_trim`, `_tent_commit`, `_entry_commit`, `on_wheel` ...) run on a stand-in row through scripted and random mouse / key sequences, with the state after every step; plus thumbnail layouts (`tools/dump_timeline_fixtures.py`, Phase 4) | 15 sequences, 2530 steps |
| `files.json` + `files/` | Sample `settings.json` (from this PC, geometry blanked), `presets*.json` with what `load_presets` makes of them, and three `.clipproj` files | - |

## Case format

- Most entries are `{"args": [...], "result": ...}`. When Python raised, the entry has
  `"error": "Type: message"` in place of `"result"`.
- **`globals`**: the filter builders read four module-level dicts (`AUD`, `VHS`, `SLOW`, `ZOOM`).
  Each case that depends on them records their exact values. The C# test must apply those values before
  calling. In C# these should become an explicit `EffectSettings` parameter, not globals.
- **Clips** in `graphs.json` / `exports.json` come in two forms:
  `row` is the timeline-level input (start, end, raw `sections`, raw `zooms`), and `clip` is the dict that
  `ClipRow.clip()` builds from it (visible sections turned into `pieces`, zooms clipped to the trim).
  Test both steps.
- Tuples are written as JSON arrays. NaN or infinity are written as `{"float": "nan"}`.

## The fake export (`exports.json`)

Every simulated ffmpeg run "writes" a file of
`(video kbps * 1000 + audio bps * tracks) * duration / 8 * overshoot` bytes.
The `overshoot` list in each case's `fake` field gives the factor per successful run.
`fail_hw` makes every run that uses `-hwaccel` fail (tests the "no hw decode" retry).
`fail_all` makes every run fail. Use the same model in the C# test double for the ffmpeg runner.
The pass search should then produce the same bitrates, labels and final size.

## Traps for the C# port (the fixtures check all of these)

1. **Number formatting.** Filter strings use Python `:.3f`, `:.4f`, `:.5f`, `:g` and `%.3f`.
   .NET `ToString("F3", CultureInfo.InvariantCulture)` rounds the exact binary value the same way on
   .NET Core 3.0+, but `:g` is **not** the same as .NET `"G"`. Write a `PyFormat.G(double)` helper.
   `python_format` in `helpers.json` lists tricky values (`2.675`, `0.0005`, `1e-7`...).
2. **`Fraction(x).limit_denominator(1001)`** turns fps values into `fps=N/D`. Port the algorithm
   (best rational approximation, continued fractions) exactly. Do not round with doubles.
   See `fraction_limit_denominator_1001`.
3. **Glitch transition randomness.** `glitch_parts` uses `random.Random(seed)` with `uniform` and `choice`.
   The C# port needs a Mersenne Twister seeded like Python's `init_by_array`. `uniform(a, b)` is
   `a + (b - a) * random()`, where `random()` is a 53-bit float from two 32-bit outputs. `choice` uses
   `_randbelow` with `getrandbits`. Without these the glitch bands differ. Seeds: `i * 31 + 7` in exports,
   `7` in the hover preview.
4. **Rounding.** `fmt_elapsed` uses Python `round()` (banker's rounding: `0.5 -> 0`, `2.5 -> 2`). Use
   `Math.Round(x, MidpointRounding.ToEven)`. `int()` truncates toward zero (`(int)` in C#).
5. **Ints vs floats in output.** Some values print as int (`{W}`, `{ms}`) and others as floats. Follow
   the Python types, not the C# ones.
6. **Command arguments are lists.** Compare argument arrays element by element, not joined strings
   (paths contain spaces, for example `C:\clips\a b.mp4`).

## Not covered here (check these by hand in later phases)

- Project loading (`load_project`, `_load_clips`, `_apply_project_settings`) lives in the Tk `App`.
  `files/*.clipproj` are inputs only. The rules to port, from the reference around line 5516:
  clips with a missing file are skipped (`rel` is tried relative to the project folder);
  `0 <= start < end <= dur + 0.01` or the trim is ignored; speeds are clamped to 0.1-100; zoom `w` is clamped to
  0.1-1 and `x`/`y` to `0..1-w`; a setting is only applied when it has the right type and an allowed value;
  `active` must be a valid index or it falls back to 0.
- The RIFE frame extraction and interpolation commands (`_rife_prepare`) need RIFE installed.
- `probe()` parsing of real ffprobe JSON. Add a few real ffprobe outputs in Phase 2.
- Timeline UI behaviour (snapping, dragging, zoom/scroll). Use the README and CHANGELOG as the checklist.
