# Research: an "AI editing" module (MCP) that edits clips from plain requests

Status: research only, nothing implemented. Date: 2026-11-19. The owner's idea: a module for people who use
Claude (Claude Code / the Claude desktop app) that lets them say what they want and have the clip edited, e.g.

- "Save the part where I fall over, with a few seconds before and after."
- "Speed up the boring part for context, play the miniboss kill at normal speed, cut the rest."
- Later: "fix the brightness and colours".

Marks: **[tested]** = checked here on the bundled ffmpeg; **[memory]** = my own knowledge, not verified now
(check before building on it). No prototype was written and nothing was measured on real clips.

## 1. What Claude can and cannot perceive

- It sees **still images**, not video. A clip has to be turned into frames: single frames, or a "contact sheet"
  (many small frames in one image, each with its time written on it).
- It cannot **hear** anything. Sound only helps as numbers the app works out (loud moments, silence), or as text
  from speech recognition (not in our ffmpeg; see section 7).
- It knows games in general (what a boss health bar, a death screen or a ragdoll fall looks like), so visible
  events work best. Taste ("the fun parts") works worst.

So the prompts above are realistic when the event is **visible**: a fall, a death screen, a boss bar vanishing,
"Victory" text. They are weak for "when my friend screams" or "the best moment".

## 2. How a request is answered (coarse to fine)

1. **Overview.** The tool makes contact sheets for the trimmed part: e.g. one frame every 2 s, 16 frames per
   sheet. A 2-minute clip is 4 sheets. Claude reads them and names candidate times ("ground around 1:12").
2. **Hints first (optional).** Before or with the overview, the tool lists moments that stand out in numbers:
   loud peaks, sudden picture changes, long quiet stretches. Claude checks those first.
3. **Close look.** Frames every 0.25 to 0.5 s around each candidate find the exact second.
4. **Edit.** Claude sets trims and speed / zoom sections through the same project model the app uses, with the
   padding the user asked for ("a few seconds" = say 3 s, clamped to the clip).
5. **Review.** The user sees the result on the timeline in Clip Shrinker and changes or exports it. The agent
   proposes; the user decides. Exporting from the agent is possible but should not be the default.

Short events can fall between overview frames (a fall that lasts 1 s at one frame every 2 s). The hints in step
2 and a denser overview for short clips (1 frame per second under 1 minute) cover most of that.

## 3. The bundled ffmpeg has almost no analysis filters [tested]

Present: `scale`, `pad`, `select`, `fps`, `trim`, `concat`, `xstack`, `thumbnail`, `aresample`, `format`.
Missing: `scdet` (scene change), `ebur128` / `astats` / `volumedetect` / `silencedetect` (loudness),
`signalstats` (brightness / colour statistics), `tile` (contact sheets), `drawtext` (time stamps on frames).

Two ways out:

- **A. Work the numbers out in C# (recommended).** ffmpeg already decodes to raw pixels and raw PCM through
  pipes (the preview does this today). From that, C# can compute:
  - loudness per 0.1 s (RMS of the PCM) and its peaks;
  - picture change per frame (difference of tiny 32x18 grey frames), which finds cuts, deaths, menus;
  - brightness and colour statistics per frame (for auto picture adjustments, section 6);
  - contact sheets: place small RGB frames in a grid and write a PNG (zlib is in .NET), times drawn with a tiny
    built-in digit font, so no FreeType / `drawtext` is needed.
  No ffmpeg rebuild, no new licences, and it is unit-testable with the existing test media.
- **B. Rebuild ffmpeg with `scdet`, `ebur128`, `signalstats`, `tile`, `drawtext`.** All are FFmpeg's own
  (LGPL) filters; `drawtext` also needs FreeType (FreeType licence or GPL, so use it under the FreeType licence)
  [memory]. Less C#, but a bigger exe and another rebuild. Only worth it if A turns out too slow.

## 4. The MCP tools

The CLI (`ClipShrinker.Cli`) gets a new mode, `clipshrinker mcp`, that talks MCP over stdin / stdout. Claude
Code or the desktop app starts it as a local MCP server [memory: `claude mcp add` for Claude Code, the
`mcpServers` section of the desktop app's config for Claude Desktop]. A C# MCP SDK exists as a NuGet package
(`ModelContextProtocol`) [memory: check its licence, version and whether it is stable before choosing it;
writing the JSON-RPC by hand is also small].

| Tool | What it returns or does |
|---|---|
| `open_project(path)` / `new_project(videos)` | Loads or creates a .clipproj; returns its clips |
| `clip_info(clip)` | Size, fps, length, HDR, audio tracks, current trims and sections |
| `contact_sheet(clip, from, to, every, cols)` | One PNG with numbered, time-stamped frames |
| `frames(clip, times[])` | A few larger frames, for a close look |
| `moments(clip)` | Loud peaks, picture changes, quiet stretches, as times with a strength |
| `set_trim(clip, start, end)` | Trims the clip |
| `add_section(clip, kind, from, to, speed)` | Speed up / slow down / zoom section |
| `clear_sections(clip)` | Removes the sections |
| `picture_stats(clip)` / `set_picture(...)` | For section 6 |
| `save_project()` | Writes the .clipproj (the app opens it) |
| `export(...)` | Optional; the same engine as the CLI's `export` |

Times in and out are seconds of the clip's source time, the same as the project file. Every editing tool
returns the clip's new state, so Claude can check what it did.

## 5. Where the edits show up

- **Version 1: the project file.** The agent edits a .clipproj; the user opens it in Clip Shrinker (or the app
  notices the file changed and offers to reload it). Simple, works without the app running, easy to test.
- **Version 2: live.** The running app listens on a local named pipe; the MCP server sends the edits there, and
  the user watches trims and sections appear on the timeline and can undo them (one undo step per agent edit).
  Nicer, but more work and more to test (two processes, focus, conflicts with the user's own edits).

## 6. Auto brightness and colour

This part barely needs AI. From the per-frame statistics (section 3A) the tool can suggest Picture adjustment
values: lift brightness when the average luma is low, add contrast when the histogram is narrow, adjust
saturation from the chroma spread. The Picture adjustments module already applies them in preview and export.
Claude's part would be to look at a before / after frame and say whether it looks right. This could also be a
plain "Auto" button in the Picture card without any MCP, which might be the better first step.

## 7. Limits and costs

- **Usage.** Every image Claude reads uses some of the user's Claude usage. Images cost roughly
  width x height / 750 tokens [memory], so a 1024x576 contact sheet is about 800 tokens; a 2-minute clip at one
  frame per 2 s is 4 sheets plus a few close-ups, which is cheap. A 1-hour recording at the same rate is 113
  sheets: too many. For long recordings use the `moments` list first, or ask the user for a rough time.
- **Sound.** No hearing. Speech-to-text is possible with FFmpeg's `whisper` filter (needs whisper.cpp and a model
  file, so a rebuild and a large download) [memory: added in FFmpeg 8.0], or a separate add-in like RIFE. Leave
  it out of version 1.
- **Precision.** About 0.25 s after the close look, which is enough for trims with padding.
- **Who can use it.** Only people with Claude Code or the Claude desktop app. It is a module: off unless wanted,
  and the rest of Clip Shrinker does not change.
- **Trust.** The agent only touches the project the user opened, never deletes video files, and does not
  export unless asked. Exports keep using the normal export settings.

## 8. Prototype plan

1. **Signals in Core (no MCP yet).** `Core/Analysis`: loudness per 0.1 s, picture change per frame, contact sheet
   PNG with time stamps, frame grabs. Unit tests on tests/media. CLI commands `sheet` and `moments` to try them
   by hand.
2. **MCP mode in the CLI.** Tools from section 4 on top of the existing project model (TimelineClip, sections,
   ProjectSave). Version 1 = project file only.
3. **Try it on real clips.** The owner's PS5 and ShadowPlay recordings with prompts like the ones at the top; note
   which work, how many images each needed, and how far off the times were.
4. **Module and docs.** An "AI editing" module (off by default), a README section with the one-line setup for
   Claude Code and the desktop app, and the reload-on-change in the app.
5. **Later.** Live link (section 5), auto picture (section 6), speech-to-text.

Step 1 is useful on its own (auto picture, a contact-sheet view, better thumbnails), so it is a safe start even
if the MCP part is dropped.
