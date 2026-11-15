CLIP SHRINKER 2.2.0 (NVENC)
===========================

GETTING STARTED
Double-click ClipShrinker.exe. Keep the "library" folder next to it: it holds ffmpeg, ffprobe, RIFE and your
settings. There is nothing to install (.NET is built into the exe). If ffmpeg.exe or ffprobe.exe is missing,
the program offers to download them once (the LGPL build from github.com/BtbN/FFmpeg-Builds).

You need Windows 10 or 11 (64-bit) and an NVIDIA GPU with NVENC (GTX 10xx or newer) on a current driver.
SmartScreen may warn about the unsigned exe; click More info, then Run anyway.

To pin it to the taskbar, start the program, right-click its taskbar button and choose "Pin to taskbar".
If you pinned 1.0.x before, unpin that first.

BASICS
- The "Dark mode" switch at the top right toggles dark and light. On first start it follows Windows.
- Hover over any button or option for a short explanation. F1 (or "Shortcuts") lists every keyboard shortcut,
  and you can change any of them there.
- Settings, window position and size, and the preview/clip list divider are saved between runs. Delete
  library\settings.json to reset everything, shortcuts included.

ADDING AND TRIMMING CLIPS
Drag videos onto the window, or click the "Add videos" strip (or the "Drop videos here" area when the list is
empty). When you add a lot at once, a small window counts them until the thumbnails are ready.

Each clip gets its own thumbnail timeline. The active clip has a blue title bar; the preview and shortcuts work
on it. Click a timeline or title bar to make it active, drag a title bar up or down to reorder (this is also the
order of a joined video), and Shift / Ctrl + click to select several clips.

Trim: drag the green (start) and red (end) handles, or press 1 / 2 to set them at the yellow playhead. Click
the timeline to move the playhead and preview that frame.

Preview: Space (or the play button on the preview) plays from the playhead to the end handle with crop, effects
and sound, looping the trimmed part. With the playhead outside the trimmed part it plays once to the end of the
video and stops. M mutes it. F (or the button at the bottom right of the preview) shows the preview full screen;
Esc leaves it. Drag the grip between the preview and the clip list to resize the preview.

Timeline zoom: Ctrl + mouse wheel zooms around the pointer, the wheel alone then scrolls, and an overview bar
at the bottom shows where you are. "Reset zoom" shows the whole clip again.

EFFECTS (the band above the thumbnails)
- S adds a speed-up section (blue), D a slow-down section (pink) at the playhead. Type the speed in the box on
  its header: 4 is four times faster, 0.5 is half speed (0.1 to 100, Enter confirms).
- Z adds a zoom section (white, header at the bottom of the timeline). While the playhead is inside it, a
  bracket on the preview marks the zoomed area; drag it to move it or drag its corners to resize it.
- The Speed Up, Slow Down and Zoom buttons under the preview do the same: click one to add the effect at the
  playhead of the active clip, or drag it onto any clip's timeline to add it where you drop it.
- Drag a section's edge lines to resize it, or its header or body to move it. Click to select a section;
  Delete or Backspace removes it, and right-click removes the one under the mouse. Sections can't overlap and
  stay inside the trimmed part.

The line under a clip's title shows its length after editing. Pitch, frame blending, RIFE AI slow motion and
zoom motion are set in "Effects". RIFE (library\rife) needs a GPU with Vulkan support.

EXPORT SETTINGS (right side)
- Size: "Max size (MB)" applies per file, or to the whole video when joining. Enter a value in "or Bitrate
  (Mbps)" instead to use a fixed bitrate; the size limit is then ignored and resolution offers only
  Original / 75% / 50%.
- Presets: create them in "Bitrate presets" (name, bitrate, optional hotkey). Pick one per clip in its title
  bar or with its hotkey, or set all clips at once with "Preset (all clips)". "Auto bitrate" means the size or
  bitrate above. Presets are used when clips are saved as separate files.
- Video: resolution, codec (HEVC by default), centered crop to 21:9, 16:9, 16:10 or 4:3 (only wider clips get
  cut), and "Convert HDR clips to SDR".
- Audio: 128k to 320k per track. ShadowPlay can record game and microphone as two tracks. "Keep separate
  tracks" saves both (Game audio / Microphone), "Mix into one track" combines them (use this for Discord,
  browsers and phones), and "Mute microphone" leaves the mic out.
- File names: prefix, suffix and save folder. Leave the folder empty to get a save dialog every time.
  Prefix and suffix can hold dynamic labels that are filled in for each file: {date}, {time}, {preset} (the
  clip's bitrate preset), {res} (output height, e.g. 1080p), {size} (the size limit) and {codec} (HEVC or
  H264). The small buttons above Prefix insert one at the cursor of the box you used last. An empty suffix
  adds nothing; put {codec} in it to get the old clip_HEVC.mp4 names.

EXPORT (Ctrl+E)
With several clips you choose between one joined video (using the transition set in "Effects") or a separate
file per clip. ClipShrinker encodes with NVENC, checks the file size, and re-encodes with a corrected bitrate
(up to 6 passes) until the file fits under the limit and lands close to it. The bar shows progress; the red
button stops the export.

PROJECTS
Ctrl+S saves the clips with their trims, effects and presets, plus the export settings, as a .clipproj file.
Ctrl+Shift+S saves under a new name and Ctrl+O opens one. Clip paths on the same drive are stored relative to
the project, so you can move the project folder. Ctrl+Z undoes; Ctrl+Shift+Z or Ctrl+Y redoes.
"Restore the last session on start" (in Effects) reopens the clips you had open when you closed the program.

NOTES
- Single clips keep their frame rate. Joined clips use the highest frame rate among them.
- Joined clips of different sizes are scaled to the largest and padded with black bars if needed.
- Resolution never goes below 50%. Auto stays at 100% unless the bitrate gets very low.
- Clips without audio get silence when joined with clips that have audio.
- Transitions are shortened automatically when a clip is too short for them.
- 1 MB = 1,048,576 bytes, so a "50 MB" result is always under 50 MiB.
- ffmpeg is a slim LGPL build of FFmpeg (settings in ffmpeg-build-settings.txt, license in ffmpeg-LICENSE.txt).
  Other components are listed in THIRD-PARTY-NOTICES.txt.
- Version history: CHANGELOG.txt, or the About button in the program.
