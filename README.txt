CLIP SHRINKER 1.0.8 (NVENC)
===========================

Start: double-click ClipShrinker.exe  (the program files live in the "library" folder - leave it next to the exe)
(first run only: if ffmpeg or Python (library\python) is missing from this folder, a setup window opens and
 downloads it - a portable Python from GitHub (python-build-standalone) (nothing is installed on your PC; winget is only the
 fallback), ffmpeg from GitHub. Then start ClipShrinker.exe again. The program uses library\python first,
 then a Python installed on the PC.
 Windows may show a SmartScreen warning for the unsigned exe: More info -> Run anyway.)
Needs an NVIDIA GPU with NVENC (GTX 10xx or newer) and a current driver.

Taskbar: to pin the program, start ClipShrinker.exe, right-click its button on the taskbar and choose
"Pin to taskbar". (Pinning ClipShrinker.exe directly from the folder gives a second icon, because
Windows then sees two different programs.) If you pinned something earlier, unpin it first.

Look: the program uses the Windows 11 style (bundled in library\sv_ttk, MIT license). The "Dark mode"
switch at the top right changes between dark and light; your choice is remembered (library\settings.json).
The first time it follows your Windows setting.
Help: hover over buttons and options for a short explanation; the "?" button (or F1) at the top right
lists all shortcuts. With no clips loaded the list shows a "Drop videos here" area you can also click.
The playhead shows its time in a small label; the progress bar shows the percentage during export.
The window stays invisible for a fraction of a second at start-up while the theme is prepared.
All settings (size, bitrate, crop, resolution, codec, audio options, export mode, transition, theme),
the window position/size and the preview/clip-list divider are remembered when you close the program
and restored on the next start. To reset everything, delete library\settings.json.
Resizing the window only changes the size of the clip list (timelines); the preview stays as it is
(drag the divider above the clip list to make the preview bigger or smaller).

Use
1. Add videos: "Add videos..." or drag & drop one or many files onto the window.
   Every clip gets its own thumbnail timeline. Drag the green (start) and red (end)
   handles to trim; click the strip to preview a frame. Use the arrows to reorder, X to remove.
   Preview: press SPACE (or the Play button) to play the active clip - the one marked with a
   triangle - with the crop applied and sound. It starts at the yellow playhead (where you last
   clicked on the timeline, or where you paused), plays to the end handle, then loops the trimmed
   part from the start handle. Press Space again to pause; Space resumes from that spot.
   Click a clip's timeline to make it the active one. Drag the divider under the preview
   (between the preview and the clip list) to make the preview bigger or smaller.
   Playback is capped at 30 fps and 1600x900 for smoothness; it does not affect the export.
   Speed sections (blue): press S to put a blue section at the yellow playhead (or in the next free
   spot). Drag its blue edges to make it longer/shorter, or drag the blue bar on top to move it.
   Type the speed in the box on the bar: 4 = four times faster, 0.5 = half speed (commas work,
   range 0.1 - 100). Enter confirms. Add as many sections per clip as you like; sections cannot
   overlap and stay inside the trimmed part. Right-click a section (or select it and press Delete)
   to remove it. The preview plays the sped-up result; the line under the timeline shows the
   final length. "Mute sound in sped-up sections" (on by default) silences faster-than-normal
   parts; untick it to keep the sped-up (higher-pitched) sound. Slowed-down parts keep their sound.
   Very high speeds (like 50x) can make the preview stutter because the video has to be decoded
   that fast; the export is not affected. The size limit is calculated on the final, shorter length.
   Long clips - zoom & scroll: hold CTRL and turn the mouse wheel over a timeline to zoom in/out
   (it zooms around the mouse pointer). While zoomed in, the plain wheel scrolls that timeline
   left/right; time marks and a small overview bar at the bottom of the timeline show where you are.
   Drag the overview bar to jump around, or drag a handle to the edge to scroll while dragging.
   "Fit" (top right of the clip) shows the whole clip again. During playback the view follows the
   playhead. When a timeline is not zoomed, the wheel scrolls the clip list as before.
2. Crop (centered): cuts equal amounts from the left and right to reach 21:9, 16:9, 16:10 or 4:3.
   Only clips wider than the chosen ratio are cropped. The preview shows the crop.
   Audio tracks: ShadowPlay can record the game and the microphone as two audio tracks. Both
   are played together in the preview. On export, "Audio tracks" decides: "Keep separate tracks"
   (default) saves both tracks in the file (named Game audio / Microphone - pick one in VLC etc.),
   "Mix into one track" combines them (use this for Discord, browsers and phones, which usually play
   only the first track). The audio bitrate applies per track.
   Clips with two tracks are marked in their title line. When joining, clips with fewer tracks get
   silence on the missing ones.
   "Mute microphone (2nd audio track)" removes the microphone: it is silent in the preview and not
   included in the export (only the game audio is kept). It applies to all clips.
3. Set max size (MB), resolution (Auto / 100% / 75% / 50%), codec (HEVC is the default) and audio bitrate.
   Size OR bitrate: type a value in "or Bitrate (Mbps)" (e.g. 20 or 12,5) if the file size does not
   matter. The size limit is then greyed out and ignored, the clip is encoded once at that video
   bitrate, and the resolution list switches to Original / 75% / 50% (Original = no downscaling,
   crop still applies). Clear the bitrate field to go back to the size limit. The bitrate is for the
   video; audio is added on top (per track). When joining clips, the bitrate applies to the whole video.
4. Choose how to export:
   - Export each clip as its own file: every file stays under the size limit.
     (One clip: asks for a file name. Several clips: asks for a folder, files get "_small".)
   - Join all clips into one video: the size limit applies to the whole joined video.
     Pick a transition (crossfade, fade through black, gradient wipe left to right,
     swipe left/right) and its length; "Hard cut" joins without a transition.
5. Export... - encodes with NVENC, checks the file size and re-encodes with a corrected
   bitrate (up to 6 passes) until it fits under the limit and lands close to it.

Notes
- Frame rate is kept for single clips. When joining clips with different frame rates,
  the highest one is used for the whole video.
- Joined clips of different sizes are scaled to the largest and padded with black bars if needed.
- Resolution is never below 50%. Auto stays at 100% unless the bitrate gets very low.
- Clips without audio get silence when joined with clips that have audio.
- Transitions are shortened automatically if a clip is too short for them.
- 1 MB = 1,048,576 bytes, so "50 MB" results are always under 50 MiB.
- If something goes wrong, clipshrinker.log in the library folder may explain why.
- ffmpeg is a slim LGPL build of the FFmpeg project (settings in ffmpeg-build-settings.txt); license in ffmpeg-LICENSE.txt.

Version history: see the Changelog (CHANGELOG.txt, or the About button inside the program).
