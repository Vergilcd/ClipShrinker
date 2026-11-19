CLIP SHRINKER 2.8.0-dev
=======================

GETTING STARTED
Double-click ClipShrinker.exe. Keep the "library" and "modules" folders next to it: library holds ffmpeg,
ffprobe and your settings, modules holds the modules (Modules button, top right) and RIFE. There is nothing to
install (.NET is built into the exe). If ffmpeg.exe or ffprobe.exe is missing, the program offers to download
them once (the LGPL build from github.com/BtbN/FFmpeg-Builds).

You need Windows 10 or 11 (64-bit). Exports are fastest on a graphics card: NVIDIA (NVENC, GTX 10xx or newer),
AMD (AMF) or Intel (Quick Sync), each with a current driver. Without one, Windows' own encoder is used, on the
graphics card when it can and on the CPU otherwise (slower, H.264 only). The program tests what works on the
PC at start-up and lists only those in Codec. The notice left of "Modules" shows "GPU Encoding is ON" (green)
or "GPU Encoding is OFF" (orange) for the chosen codec; hover over it for what was found.
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

MODULES (button at the top right)
Optional parts of the program are modules you can switch on and off, delete, and restore from GitHub. Each is a
folder in "modules" with its version and changelog (2.6 and older called them plugins; an old "plugins"
folder is moved into "modules" by itself). All of them are on after the first start:
- Timeline effects: speed up / slow motion / zoom sections and RIFE.
- Bitrate presets: per-clip bitrates with hotkeys.
- Picture adjustments: brightness, contrast, saturation and denoise. Its sun button at the bottom right of
  the preview (also in full screen) opens the sliders; type a value like 25 or -40% in a box and
  press Enter. The button turns yellow while something is adjusted. Used in the preview and the export, and
  saved with projects. Denoise removes grain from dark or low-light footage; it runs on the graphics card
  (OpenCL, any recent NVIDIA, AMD or Intel driver) and on the CPU otherwise, where exports get much slower.
  Clips that stay HDR ("Convert HDR" off) are not denoised. The Denoise row is hidden when the ffmpeg in the
  library folder has no denoise filter (an older copy): use the ffmpeg.exe and ffprobe.exe from the download.
- Sound equalizer: five bands (60 Hz to 12 kHz, -12 to +12 dB) and Volume (-20 to +20 dB) for the whole
  export. Its equalizer button next to the sun button opens the card; type a value like 3 or -6 dB
  and press Enter. Heard in the preview, used on every audio track of the export, and saved with projects. A
  limiter keeps boosted sound from clipping.
- Sound track: your own sound files on a Sounds lane under each clip's thumbnails. Drop sound files (mp3,
  wav, ogg, m4a, aac, flac, opus) on the lane where they should play, or open "Sounds" (next to Transition).
  It lists the green sounds in modules\sound-track\sounds ("Add files..." copies files there, "Open folder"
  opens it), each with its waveform: drag one onto a clip's lane, click its play button to listen, or its +
  to add it at the playhead (A adds the highlighted one). On the lane: drag a sound to move it, drag its ends to trim it, right-click or
  Delete removes it. Click a sound to select it; its volume (-20 to +20 dB) and fade in / out are set in
  Sounds. Sounds keep their normal speed inside speed sections, are heard in the preview, are mixed into the
  clip's first audio track in the export (the microphone track stays clean), and are saved with projects
  (with paths relative to the project, like the clips). A limiter keeps loud sounds from clipping.

Timeline zoom: Ctrl + mouse wheel zooms around the pointer, the wheel alone then scrolls, and an overview bar
at the bottom shows where you are. "Reset zoom" shows the whole clip again.

EFFECTS (the band above the thumbnails - Timeline effects module)
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
zoom motion are set in "Effects". RIFE (modules\timeline-effects\rife) needs a GPU with Vulkan support.

EXPORT SETTINGS (right side)
- Size: "Max size (MB)" applies per file: to each clip, or to the whole video when joining. Enter a value in
  "or Bitrate (Mbps)" instead to use a fixed bitrate (it is ignored when empty); the size limit is then
  ignored and resolution offers Original, 4K, 1440p, 1080p, 720p or 50%.
- Presets (Bitrate presets module): create them in "Bitrate presets" (name, bitrate, optional hotkey). Pick
  one per clip in its title bar or with its hotkey, or set all clips at once with "Preset (all clips)". "Auto
  bitrate" means the size or bitrate above. Presets are used when clips are saved as separate files.
- Video: resolution (Auto, 100%, 4K, 1440p, 1080p, 720p or 50%; the fixed heights never make a clip larger
  than it is), codec (HEVC by default; the list shows the encoders that work on this PC: NVIDIA, AMD, Intel
  or Windows on the GPU, and the Windows CPU encoder only when no graphics card encoder works; a project from
  another PC switches to the closest one; the open list marks the recommended one), centered crop to 21:9,
  16:9, 16:10 or 4:3 (only wider clips get cut), and "Convert HDR clips to SDR".
- Audio: Original (the clips' own audio bitrate, 64k to 320k) or 128k to 320k per track. ShadowPlay can
  record game and microphone as two tracks. "Keep as separate tracks" saves both (Game audio / Microphone),
  "Mix into one track" combines them (use this for Discord, browsers and phones), and "Mute microphone"
  leaves the mic out.
- File names: prefix and suffix. They can hold dynamic labels that are filled in for each file: {date},
  {time}, {preset} (the clip's bitrate preset), {res} (output height, e.g. 1080p, 1440p or 4K), {size} (the
  size limit) and {codec} (HEVC or H264). Click a label button to insert it at the cursor of the box you
  used last, or type its code (shown when you hover the button). An empty suffix adds nothing; put {codec}
  in it to get the old clip_HEVC.mp4 names. Untick "Keep original file name" to leave the clip's name out,
  so only the prefix and suffix name the file (e.g. 2026-11-15_HEVC.mp4). The tick is greyed out while
  Prefix and Suffix are both empty.
- Saving: "Export folder" (exports are saved there without asking; when empty, a save dialog asks at every
  export) and "Save session on exit", which keeps the open clips when you close the program and opens them
  again on the next start.

EXPORT (Ctrl+E)
With several clips you choose between one joined video or a separate file per clip. The joined video uses the
transition picked with the "Transition" button next to "Clear all clips" (shown with 2 or more clips; point at
an effect in its list to see a preview). ClipShrinker encodes with the chosen codec, checks the file size, and
re-encodes with a corrected bitrate (up to 6 passes) until the file fits under the limit and lands close to it. The bar shows progress; the red
button stops the export.

PROJECTS
Ctrl+S saves the clips with their trims, effects and presets, plus the export settings, as a .clipproj file.
Ctrl+Shift+S saves under a new name and Ctrl+O opens one. Clip paths on the same drive are stored relative to
the project, so you can move the project folder. Ctrl+Z undoes; Ctrl+Shift+Z or Ctrl+Y redoes.
"Save session on exit" (under SAVING) reopens the clips you had open when you closed the program.

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
- Updates: the About window checks GitHub when it opens and shows next to the link whether a newer version
  is out (green light). Nothing is downloaded; updating is up to you.
