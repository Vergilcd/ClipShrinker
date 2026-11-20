# Research: audio denoiser, "music filter", contrast/brightness

Status: research only, nothing implemented. Date: 2026-11-15. Checked against the bundled
`Dev\library\ffmpeg.exe` (git-2026-10-24-a881f0a, minimal `--disable-everything` build).

Rule from CLAUDE.md that shapes everything below: exports must stay identical to 1.0.8 unless the user opts in.
So every new filter is off by default, adds **no** filter text to the graph when off, and the golden fixtures
must stay green untouched.

## 1. Filter availability

"Bundled" was tested by running the filter (`-h filter=NAME`, plus real runs on `tests/media/a_1080p60.mp4`).
"BtbN LGPL" is **not verified here** (it would mean downloading a ~100 MB zip; I did not). It is from my knowledge
of FFmpeg's source: all filters below are native FFmpeg code with no external library, so the stock BtbN
`win64-lgpl-shared` build, which enables every native filter, should contain them. `arnndn` needs a model file at
runtime but no library. Confirm with `ffmpeg -filters` on the downloaded build before relying on it.

| Filter | Bundled | BtbN LGPL | Use |
|---|---|---|---|
| afftdn | NO | yes (expected) | FFT denoiser, `nr`, `nf`, `tn` (adaptive noise tracking) |
| anlmdn | NO | yes (expected) | non-local-means denoiser, slow |
| arnndn | NO | yes (expected), needs `.rnnn` model | RNNoise neural denoiser, best for voice |
| highpass / lowpass | YES | yes | rumble / hiss cut (also per-feature, tested OK) |
| pan | YES | yes | channel maths: mid/side, centre extract or cancel (tested OK) |
| volume (`enable=` timeline) | YES | yes | |
| amerge, channelsplit, channelmap, amix, alimiter | YES | yes | |
| equalizer / anequalizer / firequalizer | NO | yes | band EQ |
| compand / acompressor / dynaudnorm / loudnorm / agate | NO | yes | dynamics |
| extrastereo / stereotools / afftfilt | NO | yes | |
| eq | NO | yes | brightness, contrast, saturation, gamma |
| curves / colorlevels / colorbalance / colorcorrect / vibrance | NO | yes | |
| lutyuv | YES | yes | per-pixel expression on Y/U/V (tested OK) |
| colorchannelmixer | YES | yes | RGB matrix (tested OK) |
| hue | YES | yes | saturation `s`, hue; `b` is a hue-brightness, not a real brightness |
| normalize | YES | yes | auto per-frame RGB levels stretch |
| zscale / colorspace / tonemap | YES | yes | already used for HDR |

Consequences:
- Feature 1 (denoise) cannot be done properly with the bundled ffmpeg; it needs either a rebuild of the custom
  ffmpeg with `--enable-filter=afftdn` (small, a few KB; also `arnndn`) or the BtbN fallback.
- Feature 3 (contrast/brightness) can be done with the bundled build via `lutyuv` (and `eq` if the build gets
  `--enable-filter=eq`).
- Feature 2 depends on the interpretation; the cheap ones work with `pan` + `highpass`/`lowpass`.

The cleanest long-term fix is one rebuild step: add `afftdn`, `arnndn`, `eq` (and optionally `equalizer`,
`acompressor`) to the `--enable-filter=` list. They are LGPL, so licensing is unchanged. THIRD-PARTY-NOTICES
needs no change; the model file for arnndn (see below) needs a licence line.

## 2. Where it goes in the code

The reference graph (GraphBuilder.Build, per clip):

```
video: [i:v:0] crop,scale,pad,setsar,(tonemap)  <- ClipVfilters   (Filters.cs:270)
       -> (segmented: setpts, SegVideo speed pieces) -> zoom -> fps,format=yuv420p,settb,setpts
audio: [i:a:k] aresample=48000,aformat=fltp:stereo,apad,atrim,asetpts   (GraphBuilder.cs:62)
       -> (mix) -> SegAudio speed pieces (atempo etc.) or anull -> zoom whoosh
```

Preview mirrors it in `PreviewCommands.Play` (video, RGB24 pipe) and `PreviewCommands.PreviewAudio` (44.1 kHz).
Both read an `EffectSettings` record (Model.cs:91). The sane way to keep preview and export identical is the
way the existing effects do it: add the options to `EffectSettings` (and `ExportSettings`/`FromExport`), and put
each filter text into ONE helper in Filters.cs that both builders call.

Proposed helpers (not written):

```csharp
Filters.AudioCleanup(EffectSettings fx, int rate) -> string?   // null when everything is off
Filters.VideoGrade(EffectSettings fx)             -> string?   // null when off
```

### Video grade placement
- Export: append in `ClipVfilters` after `tonemap` (so the grade acts on SDR picture, same as what you see), i.e.
  just before the per-clip tail. This is before speed pieces and zoom, so slow-mo blending (`SlowBlend`/RIFE) and
  zoom crops see graded frames. Applying it once per clip is correct; apply to the xfade output would be wrong
  because transitions would then be graded unevenly.
- Preview `Play`: three branches. The `gpu` branch (`scale_cuda`...) and the plain branch must be taught about
  the grade: simplest is "grade on => gpu = false", and add the filter after `scale` (and after tonemap) in the
  plain and segmented branches. The preview scales first with `fast_bilinear` and export crops/scales/pads first;
  a pointwise LUT grade is resolution-independent, so results match.
- Bit depth: after HDR tonemap the frames are 8-bit-ish, but plain 10-bit SDR can reach the filter as yuv420p10.
  Write the lutyuv expression against `maxval` instead of 255 (`val/maxval`, `clip(..., minval, maxval)`) so it
  works at any depth. Test with the `d_hdr10.mp4` fixture and a 10-bit clip.

### Audio placement
- Export: directly after the `aresample=48000,aformat=...` link at GraphBuilder.cs:62 (before `apad`/`atrim`, and
  therefore before any `atempo`). Denoising before speed changes matters: atempo shifts the noise spectrum, and
  RNNoise/afftdn track noise at the original rate.
- Preview `PreviewAudio`: same spot, after its `aresample=44100,aformat` link. Note the fast path
  `plain && naudio == 1 && zt.Count == 0` uses `-map 0:a:0` with no filter; it must be bypassed when audio
  cleanup is on. `EffectSample` (the effect-sound preview in Effects window) can call the same helper so the user
  hears the denoiser on a sample.
- Per clip, per audio track (multi-track clips: apply to every track, or only track 0 to save time; decide in UI).
- Rate: filter text has to be rate-aware only for `arnndn` (it resamples internally, so no) and `afftdn`
  (rate-independent parameters). The helper does not need the rate except for cutoff clamping.

## 3. Feature 1: audio denoiser

Candidates:
- `afftdn=nr=12:nf=-30:tn=1`: spectral gating, fast (about real-time x 100+), no model file, handles steady noise
  (fans, hiss, mic floor). Artifacts ("watery" sound) at high `nr`. Presets: Light nr=8, Medium 12, Strong 20.
- `arnndn=m=model.rnnn`: neural, very good for voice over keyboard/fans, handles non-stationary noise. Needs a
  `.rnnn` model (the rnnoise-models repo, e.g. `bd.rnnn`, `sh.rnnn`, BSD-3 licensed, ~90 KB). Mono 48 kHz internally,
  so insert after our `aresample=48000`. Model path has to be escaped for the filter graph (Windows `C:\` colon and
  backslashes: use forward slashes and `\\:`); the safest approach is to copy the model next to the exe and use a
  relative path or `-filter_complex_script`.
- `anlmdn`: good quality but 5-20x slower than afftdn; skip.
- `highpass=f=80`: pre-step to remove rumble before the denoiser (already available).

Cost: small for export time. Measured here: cheap audio filters (pan/highpass/lowpass) process a 20 s clip in
under 0.1 s. afftdn and arnndn were NOT measurable here (not in the bundled build); from general experience both
run far faster than real time, so expect seconds, not minutes, for a 5-min clip, but measure after the rebuild.
NVENC video encode dominates. No effect on video.

Recommendation: **build it with `afftdn` first**, controls: Off / Light / Medium / Strong (one dropdown) plus an
optional "also cut rumble (highpass 80 Hz)" tick. Offer `arnndn` as "Voice (AI)" only if the user wants more
and is fine with a bundled ~90 KB model file. Prerequisite: rebuild the slim ffmpeg with `afftdn` (and `arnndn`).
Without a rebuild the only option is requiring the BtbN download, which defeats the point of the slim build; so
do the rebuild. Because the BtbN fallback should have both, the option can be disabled with an explanatory tooltip
when `ffmpeg -filters` lacks the filter (probe once at startup, same as how NVENC presence is probed).
Default Off, so exports stay 1.0.8-identical.

## 4. Feature 2: "music filter"

"Music filter" is ambiguous. Interpretations, from most likely to least:

A. **Reduce game/background music so voice and game sound stand out.** This is the usual wish for gameplay clips.
   Without source separation ffmpeg can only approximate it.
   - Speech band emphasis: `highpass=f=150,lowpass=f=6000`. Cheap, bundled, works, but cuts bass/treble of
     everything.
   - Centre-vs-side: music is usually wide stereo, voice and many game sounds are centre. Keep centre:
     `pan=stereo|c0=0.5*c0+0.5*c1|c1=0.5*c0+0.5*c1` (mono sum; tested OK) mixed with the side removed. Fully
     bundled. Result is mono, which loses game stereo imaging; a milder version blends mono and original.
   - Real reduction needs AI separation (demucs/spleeter) which is far outside ffmpeg; not recommended.
B. **Remove the vocals / keep the music** (karaoke): `pan=stereo|c0=c0-c1|c1=c1-c0`. Bundled, tested. Only works
   on stereo music with centred vocals; rarely what a game-clip user wants.
C. **Add music** to a clip (background track, ducking under voice). Needs `sidechaincompress` / `amix` + a file
   picker; `sidechaincompress` and `acompressor` are not in the bundled build. This overlaps the already noted
   "sound-effect library (needs audio tracks in timeline)" idea; do it together with that, not separately.
D. **Music-safe / copyright-friendly mute**: mute sections. Already covered by trim/mute.

Cost: A and B are pure `pan`/`highpass`/`lowpass`; effectively free (about 0.1 s per 20 s of audio here).

Recommendation: **ask the owner what they mean before building.** If it is A, ship a small "Voice focus" dropdown
(Off / Mild: highpass 120 + lowpass 9000 and a 70/30 blend of centre-sum; Strong: mono centre only), set honest
expectations in the tooltip ("cannot truly separate music"), and use only bundled filters. If it is C, fold it into
the audio-track/sound-effect project. Do B only if explicitly asked. Default Off. Same graph position as the
denoiser (run denoise first, then voice focus), shared in `Filters.AudioCleanup`.

> Update: the owner meant interpretation A in its strong form: removing background music (e.g. Spotify) from the
> recording, as an add-in like RIFE, plus vocal removal. That is researched in `music-removal-research.md`
> (the filter-only "voice focus" above is only the cheap fallback).

## 5. Feature 3: contrast and brightness

Candidates:
- `eq=contrast=1.1:brightness=0.03:saturation=1.1:gamma=1.0`: the standard, one filter, also saturation and
  gamma. NOT in the bundled build; add `--enable-filter=eq` (tiny, LGPL-safe, GPL-free: `eq` is native in
  libavfilter; the separate `eq2` is GPL, avoid). BtbN LGPL should have it.
- `lutyuv`, bundled and tested: `lutyuv=y='clip((val-maxval/2)*C+maxval/2+B*maxval,minval,maxval)'`
  (C = contrast factor, B = brightness offset in 0..1 of range). Works on any bit depth if written against
  `maxval`; chroma untouched, so no colour shift. Slightly less friendly for limited-range blacks (1.0 contrast
  around mid grey, not 16/235 centre); acceptable.
- `hue=s=1.1` for saturation if wanted (bundled, tested).
- `curves`/`colorlevels`: better for presets ("Cinematic", "Punchy") but need a rebuild; skip for now.
- `normalize`: bundled, auto levels per frame with smoothing; "Auto" button candidate, but it flickers on
  scene cuts; if offered, `smoothing=30` or so. Not recommended as the default.

Cost (measured on 1080p60 H.264, 10 s, CPU filter pipeline, decode + filter, no encode):
`null` 0.94 s, `lutyuv` 1.13 s (+20%), `colorchannelmixer` + `hue` 1.84 s (+95%). Compared with NVENC encode time it
is small, but note it keeps frames on the CPU: the current plain preview uses `scale_cuda`; the grade forces the
CPU branch in preview (slower than `scale_cuda`; not measured). Export already uses CPU `scale`/`pad`, so export speed
is little changed.

Recommendation: **build with `eq` after adding it to the ffmpeg rebuild** (Brightness, Contrast, Saturation sliders,
Reset button, off when all at neutral values so no filter text is emitted). If the rebuild is not wanted,
the same sliders map to `lutyuv` (+ `hue` for saturation) with no rebuild and the same look to within rounding;
write the helper so the backend can switch by probe result. Per-clip grading (each clip has its own light) is
valuable for game clips from different games; start with a global Effects-window section, with a per-clip
override as a later addition. Live preview: the grade can be added to `Play` easily; a split before/after view
is a UI nicety.

## 6. UI sketch

Effects window gets a new section "Picture and sound" (below the existing effect sections; matches the
`EffectSettings` pattern, with `EffectSample` for hearing audio changes):

```
Sound cleanup      [ Off | Light | Medium | Strong ]   [x] cut low rumble     [Hear sample]
Voice focus        [ Off | Mild | Strong ]            (? cannot separate music from voice)
Picture            Brightness [-----|-----]  Contrast [-----|-----]  Saturation [-----|-----]  [Reset]
```

Per-clip overrides: a later step; the project file (Projects) would need a nullable per-clip settings
field. Persisted in presets with the other effect settings; defaults must equal "Off", so that old presets and
the golden fixtures produce no new filter text.

## 7. Testing notes (for whoever builds it)
- Unit tests: the helpers return null when off; `GraphBuilder.Build` output with defaults equals the golden
  fixtures (they already do for 1.0.8 behaviour); with on, snapshot-test the exact strings.
- Preview/export match: assert the same helper text appears in both `PreviewCommands.*` and `GraphBuilder`.
- Run a real encode on `a_1080p60.mp4`, `b_ultrawide_2tracks.mov` (two tracks, check each track) and
  `d_hdr10.mp4` (grade happens after tonemap; 10-bit depth). Audio checks are done by ear by the owner.
- Make the missing-filter probe a unit test with a fake ffmpeg output.

## 8. Summary

| Feature | Do it? | How | Needs |
|---|---|---|---|
| Audio denoiser | yes | `afftdn` presets (+ optional `arnndn` voice mode, + `highpass`) at GraphBuilder.cs:62 and PreviewAudio | rebuild slim ffmpeg with `afftdn` (+`arnndn`, + model file) |
| Music filter | ask first | A "voice focus" via `pan` + `highpass`/`lowpass`; B vocal removal via `pan`; C add/duck music belongs with sound-effect library | nothing for A/B (bundled); C needs `sidechaincompress` |
| Contrast / brightness | yes | `eq` (preferred) or `lutyuv` + `hue` (works today), in `ClipVfilters` after tonemap, and in `Play` after scale | rebuild with `eq` is optional |
