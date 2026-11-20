# Research: removing background music (Spotify etc.) from a clip, and vocal removal

Status: research only, nothing implemented. Date: 2026-11-15. Follow-up to section 4 of
`later-effects-research.md` (the owner's meaning of "music filter": music that was playing while the game was
recorded, e.g. Spotify, should be muted relative to the game; as an optional add-in like RIFE; vocal removal is
also wanted as a function of its own).

How to read the "checked / memory" marks: **[checked]** = found in a web search during this session (links in
section 8); **[tested]** = run here on the bundled ffmpeg; **[memory]** = from my own knowledge, not verified now.
Where search results conflicted I say so. Nothing about speed or quality was measured on the owner's real clips,
and no model was downloaded or run.

## 1. The problem in one paragraph

A ShadowPlay-style clip has the game and the music already mixed into one AAC track. Removing one part of a
mix means either (a) knowing exactly what the music signal is and subtracting it (reference cancellation), or
(b) a model that has learned what music sounds like and pulls it out (source separation), or (c) never mixing them
(separate tracks while recording). Plain ffmpeg filters cannot do (a) or (b); the bundled build has no
`afftdn`, `afir`, `sidechaincompress` and so on [tested]. Whatever is chosen is a pre-pass in a separate add-in
exe, like RIFE.

## 2. Approach 1: reference-based cancellation ("how noise cancelling works")

Idea: identify the song, get the original audio, line it up with the recording, work out gain and EQ, subtract.

Steps and what each really needs:
1. **Identify.** Chromaprint fingerprints plus the AcoustID lookup, or a Shazam-like service.
   - Chromaprint: its own code is MIT but it contains FFmpeg-derived parts under LGPL 2.1, so it is treated as
     LGPL 2.1 as a whole; do not build it with the GPL FFTW3 backend [checked; one source (Essentia) lists it as
     GPL with a commercial option, so confirm in the repository's own LICENSE before shipping].
   - AcoustID web service: described as free for non-commercial use, and the exact terms for an app were not
     found [checked: secondary sources only]. Read acoustid.org's terms before relying on it. Commercial services
     (ACRCloud etc.) are paid [memory].
2. **Get the original.** This is the blocker. Spotify streams are DRM-protected, there is no legal way to read the
   audio of a track from the Spotify client or API (the API gives metadata; 30 s preview URLs were being
   withdrawn for new apps [memory, unverified]). Ripping it is not something to build into the app. The only
   clean variant is "the user points to their own copy of the song (a file they own)", in which case fingerprinting
   is not even needed; the user picks the file.
3. **Align.** Cross-correlate (FFT, GCC-PHAT) to find the offset. Recording and playback clocks differ, so there is
   slow drift (tens of ppm = a few ms per minute) and possibly a different sample rate (44.1 vs 48 kHz); the
   alignment must be tracked over time, not found once [memory, standard DSP].
4. **Estimate the path.** Gain, EQ and delay of Windows' audio chain, with an adaptive filter (NLMS or per-bin
   Wiener/least squares), which is the same maths as acoustic echo cancellation [memory, standard DSP].
5. **Subtract.** `recording - estimated music`.

What breaks it in practice:
- **Not the same audio.** Spotify plays lossy Ogg Vorbis (or AAC on some platforms) at 96-320 kbps, plus
  loudness normalisation toward -14 LUFS (Premium can pick -19 or -11) [checked], so a local FLAC/MP3 differs in
  level and in codec noise. Level is estimable; codec noise is not. Remastered or different edits of the same song
  do not cancel at all [memory].
- **Crossfade/gapless/shuffle/radio edits and mid-song volume changes** by the user or by the game (ducking music)
  make the gain time-varying; the filter has to adapt quickly, which also makes it eat into the game audio
  [memory].
- **ShadowPlay re-encodes the mix to lossy AAC.** The coding noise of the recording is not in the reference, so
  cancellation depth is limited by it; the leftover is a quieter "ghost" of the music, typically 10-25 dB
  reduction at best [memory/estimate, not measured].
- **Game audio overlaps the music** in time and frequency, and a naive subtraction leaves artefacts wherever the
  estimate is wrong; any non-linear step in the chain (limiters, spatial audio, Windows enhancements; OBS notes
  Windows 11 audio enhancements can muffle capture [checked]) breaks the linear model.
- **Exactly one song at a time**; a clip spanning a track change needs per-song handling.

Assessment:
- Feasibility: possible only in the "user supplies the original file" form; automatic identify-and-fetch is
  not legally available for Spotify.
- Quality: potentially the best of the three when everything matches (deep cancellation of the music, game audio
  intact), and poor to useless when it does not.
- Add-in size: tiny (no model), a native DSP exe or even C# code, < 5 MB, plus Chromaprint if fingerprinting is kept.
- Time: fast, near real time or better.
- Licence: our own code; Chromaprint LGPL if used; no model weights.
- Plug-in: same pre-pass shape as RIFE (section 5), with an extra "choose the song file" step in the UI.
- Effort: roughly 3-4 weeks and a high risk of "works on my test clip only". Needs real test recordings with a
  known reference to tune.
- Verdict: **do not build** as a general feature. Reconsider only as a niche "I have the song file" mode.

## 3. Approach 2: AI source separation, no reference

Candidates: Demucs / HTDemucs, Spleeter, Open-Unmix, MDX-Net / UVR models, BS-RoFormer.

**The key limitation: these models split a song into vocals/drums/bass/other.** They are trained to take apart
music, not to separate "music" from "game audio". On a mix of Spotify plus game they would:
- put vocals into the vocals stem (this also catches voices in the game and voice chat);
- put music drums and bass into those stems, but also game explosions/thumps;
- dump the rest, game effects and the music's harmonic parts together, into "other".
So there is no stem that is "the game". Dropping drums+bass+vocals leaves a mix of game sound and the music's
harmonic bed. Expect partial reduction with artefacts, not clean removal. This is my reasoning from how the
models are trained [memory], not something tested; it must be tested on the owner's real clips before building
(section 7, spike).

The better-fitting model family is **speech / music / effects separation** (trained on cinematic data, the
"Divide and Remix" dataset, models such as "Bandit"), whose "effects" stem is much closer to game sound
[memory; I did not verify that usable weights and a permissive licence exist]. If the spike shows regular
Demucs is not good enough, check this family next.

Per model (sizes and speed marked as checked or memory):

| Model | Size | Speed | Licence |
|---|---|---|---|
| HTDemucs (4 stems) | ~316 MB as one ONNX file; the fine-tuned 4-model bag ~1.26 GB [checked, third-party ONNX repos] | CPU: about 1.5x track length per Demucs' README, so ~5-8 min for a 5-min clip [checked, secondary]; one third-party report: real-time factor 0.20 on an Apple M4 Pro CPU [checked]; NVIDIA GPU: roughly 10-20x faster than CPU, ~20-40 s [memory, unmeasured] | Code MIT. Weights: most repackagers say MIT, one source says no explicit grant was found for the weights [checked, conflicting]. Check the upstream repo. Upstream `facebookresearch/demucs` was archived 2025-01-01; the maintained fork is `adefossez/demucs`, bug fixes only [checked] |
| Spleeter (2/4/5 stems) | ~100-200 MB [memory] | faster than Demucs, lower quality [memory] | Code MIT; weight licence flagged as unresolved in an open issue [checked] |
| Open-Unmix | ~100-200 MB [memory] | similar to Spleeter | No explicit licence found for main models, the larger "UmXL" model has a non-commercial note [checked, weak] |
| MDX-Net / UVR models | ~50-200 MB each [memory] | similar or slower than Demucs | Redistributions claim MIT; not verified upstream [checked, weak] |
| BS-RoFormer / Mel-Band RoFormer | ~200-900 MB [memory] | slowest, best quality for vocals | One source labels weights CC-BY-NC-SA 4.0 (non-commercial); another mirror says MIT [checked, conflicting]; treat as non-commercial until confirmed |

Packaging for a Windows add-in with no Python for the user:
- **ONNX Runtime in the add-in exe** (or inside the app): `Microsoft.ML.OnnxRuntime.DirectML` works on any
  DX12 GPU (NVIDIA included) without CUDA/cuDNN downloads; the CUDA provider needs ~1 GB of NVIDIA libraries
  [memory]. Third-party ONNX exports of HTDemucs exist and are described as running on ONNX Runtime CPU/CUDA/
  DirectML with a provider switch [checked, but I found no Windows benchmark]. Caveat: many Demucs ONNX exports
  keep the STFT/iSTFT outside the graph, so we would write that step ourselves [memory].
- Alternative: a standalone C++ port such as demucs.cpp (GGML) [memory, unverified] as a single exe, CPU-only and
  slower.
- Alternative: a PyInstaller-style frozen Python exe with PyTorch is 1-3 GB and fragile; reject.
- Add-in download: ~20 MB (runtime) + 100-350 MB (model). RIFE for comparison: the installed folder here is 17 MB.
  So this add-in would be 10-20x larger than RIFE.

Assessment:
- Feasibility: technically sure; fitness for "music vs game" unknown until the spike.
- Quality: good for vocals (so vocal removal, section 6); moderate and artefact-prone for music vs game.
- Time: GPU 20-40 s per 5-min clip, CPU ~5-8 min [estimates, see table]. Only the audio range of the clip
  is processed (not the whole recording), and only once per clip (cached, section 5).
- Licence: code MIT; weights must be checked file by file; avoid the BS-RoFormer family unless confirmed.
- Effort: ~2 weeks after the spike (section 9).

## 4. Approach 3: prevention (record the music on its own track)

- **ShadowPlay / NVIDIA overlay:** can put microphone and system audio on two tracks; I found nothing showing
  per-application tracks (users ask for them) [checked; one competitor page claims no separate tracks at all,
  which conflicts]. So Spotify and the game stay mixed. Unusable for this need.
- **OBS Studio:** Application Audio Capture sources (Windows 10 2004+ and Windows 11) give per-app capture, one
  source per app, and each source can be routed to its own track in Advanced output settings [checked]. Known
  quirks: needs the global Desktop Audio source disabled to avoid echo, some apps need a virtual cable, per-app
  capture can pick up multi-device audio, and a forum post says replay buffer does not combine with multiple
  tracks in advanced mode [checked, one post: unclear/stale, verify]. The user's workflow is ShadowPlay-style clips,
  so this means changing recording software.
- **Windows alone:** Settings > Volume mixer sets per-app levels and per-app output device, so routing Spotify to
  another device (or a virtual cable) keeps it out of the game's capture [memory]. This only helps before the
  clip is made.
- **What ClipShrinker can do cheaply:** it already reads multiple audio tracks (`ExportClip.AudioTracks`,
  `GraphBuilder.OutTracks`, the `mix` option). A **per-track volume and mute control** in the export (graph:
  `volume=0` or `volume=0.3` on `[i:a:k]`, bundled filter [tested earlier]) makes any clip from an OBS
  multi-track recording instantly "music-free". Cost: negligible; effort 2-3 days including preview and tests.

## 5. How an add-in would plug in (modelled on RIFE)

What RIFE does today (read from the code):
- `RifeInstall.Find(dir)` locates `rife-ncnn-vulkan.exe` and a model folder inside the library folder
  (`Dev\library\rife`, 17 MB here); `RifeDownloader` fetches the zip from a release URL using HTTP range requests
  (`HttpRangeStream`) to take only the needed members, with progress and cancel (`Rife/RifeDownloader.cs`).
- `Encoder.RifePrepare` (Encoder.cs ~l.166) is a pre-pass run before the graph is built when `fx.SlowRife` is set
  and RIFE is installed; otherwise it logs a fallback and the export continues with frame blending. It extracts
  frames, runs the exe via `IToolRunner` with progress parsing, and records `RifeInput`s as extra ffmpeg inputs;
  `GraphBuilder.Build(..., rife)` swaps those inputs in. Temp folder cleaned in a `finally`.

Equivalent for audio ("AudioCleanup add-in"):
1. `AudioAddonInstall.Find/Download` copying the RIFE pattern (exe + model files + licence text).
2. `Encoder.AudioPrepare` (pre-pass, per clip and per audio track that the user enabled): extract
   `[Start, End]` of the track with ffmpeg to a float WAV (use the whole clip range, not the speed pieces,
   because speed changes are applied later); run the add-in exe; read back the chosen result (e.g. "everything
   except stems X" mixed to one WAV). Pre-pass progress joins the existing progress bar like RIFE's `pbase/pspan`.
3. `GraphBuilder.Build` gets an optional map `clip i, track k -> extra input index`. In the per-track line
   (GraphBuilder.cs:62) the source label becomes `[N:a:0]` instead of `[i:a:k]`; everything downstream
   (`apad/atrim`, speed pieces via `SegAudio`, zoom whoosh, mix, xfade) stays unchanged, so slow-motion and
   effects keep working and defaults emit identical graphs when the add-in is off.
4. **Preview** is the harder half. `PreviewCommands.PreviewAudio` is called per playback window with `-ss/-t` on
   the original file. The cleaned audio must therefore be a **cached file per (source path, mtime, track, model,
   mode)** for the whole clip, produced once with a progress UI ("Cleaning audio... 40%"); `PreviewAudio` then
   reads `-i cleaned.wav` with the offset relative to the clip start. Without a cache, preview would re-run the
   model on every seek. Export reuses the same cache (and its fallback when the cache is missing is to run the
   pre-pass).
5. Settings/UI: Effects window section "Remove background music" with Off / Reduce / Remove and a
   "Download add-in (~300 MB)" button when missing, same as the RIFE tick; saved in presets (default Off, so
   fixtures stay golden); hint "audio checks by ear, use the sample button" like other effects.

## 6. Vocal removal

- **Centre cancel (free, no add-in).** `pan=stereo|c0=c0-c1|c1=c1-c0` [tested: runs on the bundled ffmpeg]
  removes anything panned dead centre. For music with centre vocals it removes the voice, but it also removes
  centred game dialogue, voice chat, and bass/kick; the result is thin and nearly mono. Reasonable as a "quick
  karaoke" option (milliseconds of processing), poor for clips with game sound.
- **Separation model, vocals stem.** HTDemucs and the other models give a vocals stem directly; "vocal removal"
  = the sum of the other stems, "voice only" = the vocals stem. Much better than centre cancel for music, but
  it treats any speech as vocals, so game dialogue is removed too. Same add-in, an extra mode selector (the model
  runs once; modes are free).
- It fits the add-in in section 5 as `Mode = RemoveVocals`; the centre-cancel version can ship first as a
  bundled-ffmpeg-only option via the shared `Filters.AudioCleanup` helper from the first document.

## 7. Comparison

| | 1. Reference cancellation | 2. Separation model | 3. Prevention |
|---|---|---|---|
| Works on existing clips | only with the song file | yes | no (future clips only) |
| Quality for "music vs game" | best case high, usually poor | moderate (unproven, test first) | perfect |
| Add-in size | < 5 MB | 100-350 MB + 20 MB runtime | none |
| Time, 5-min clip | seconds | 20-40 s GPU / 5-8 min CPU (estimate) | none |
| Licence risk | Chromaprint LGPL; song source is the problem | weights unclear (BS-RoFormer: non-commercial) | none |
| Effort | 3-4 weeks, high risk | ~2 weeks after a spike | 2-3 days (track volume/mute) |
| Vocal removal | no | yes (vocals stem) | no |

## 8. Sources used for [checked] claims

- HTDemucs ONNX exports (sizes, licence statements, runtimes): https://huggingface.co/adowu/htdemucs-onnx ,
  https://huggingface.co/StemSplitio/htdemucs-onnx , https://stemsplit.github.io/demucs-onnx/models/
- Demucs repository status: https://github.com/facebookresearch/demucs (archived), https://github.com/adefossez/demucs
- Chromaprint/AcoustID licensing: https://en.wikipedia.org/wiki/AcoustID ,
  https://sources.debian.org/src/chromaprint/1.6.0-2/debian/copyright ,
  https://essentia.upf.edu/licensing_information.html
- ShadowPlay tracks and OBS application audio capture: https://obsproject.com/kb/application-audio-capture-guide ,
  https://github.com/bozbez/win-capture-audio , https://medal.tv/features/separate-audio-tracks
- Spotify loudness normalisation: https://support.spotify.com/bd-en/artists/article/loudness-normalization
- Model licences: https://huggingface.co/Best-Practice/spleeter-4stems-onnx (Spleeter issue 898),
  https://huggingface.co/AEmotionStudio/roformer-models/blob/main/README.md ,
  https://huggingface.co/Acapellas/uvr5-models , https://github.com/Deezer/spleeter

Not found: Spotify DRM/offline format and crossfade details (stated from memory); Windows benchmarks for any model;
the official AcoustID terms page.

## 9. Recommendation and effort

1. **Now, small (2-3 days): per-track volume/mute in the export and preview**, plus a README tip for OBS
   Application Audio Capture. It is the only approach that gives clean results, uses bundled filters only, and
   needs no add-in. Gets the owner "music out" for any future clip from multi-track recordings.
2. **Spike before any add-in (1-3 days):** with 3-5 real clips of game plus Spotify, run Demucs (and, if the
   result is poor, a speech/music/effects model) offline outside the app, and let the owner judge by ear. This is
   the deciding experiment: it tells us whether the separation add-in is worth ~2 weeks and ~300 MB. Also settle the
   weight licences at this step.
3. **If the spike passes: build the audio add-in** (section 5) on ONNX Runtime with DirectML, modes
   "Reduce music" and "Remove vocals", cached cleaned file used by preview and export, default Off. Effort about
   2 weeks: downloader/installer 1-2 days (copy of RIFE), runner and progress 2-3 days, graph and preview cache
   3-4 days, UI/settings/tests 2 days, GPU/CPU packaging QA 3-5 days.
4. **Vocal removal first as the free version:** centre cancel via `pan`, together with the "voice focus" idea in
   the first document (<= 1 day on top of the shared audio helper); the model-based vocals stem comes with step 3.
5. **Skip** reference cancellation (approach 1), except possibly a user-supplied-file mode much later.
