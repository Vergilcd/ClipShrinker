---
name: subtitles
description: Make an .srt subtitle file for a clip from what is said in it (and translate it if asked), with each line timed to the voice, its words checked, and progress reported to the user while it runs.
---

# Subtitles for a clip

Speech to text gets most words right, but its timing and some words need checking: follow every step.

The result is an .srt file next to the video, with the video's name (`Clip.mp4` -> `Clip.srt`), so video players
load it by themselves. A translation gets the language code before the extension (`Clip.hu.srt`). If the file
exists already, keep it (rename it to `Clip.v1.srt`) unless the user says otherwise. You write the file yourself;
the tools give the words, the times and the frames.

## 1. Plan the job and tell the user

Before the first `transcribe` call, tell the user in one short message:

- how long the kept part is and how many windows it will take (step 2);
- an estimated time. Transcribing takes about as long as the sound on the CPU (5 minutes of sound: about
  5 minutes), much less on a GPU. Checking frames and writing the file adds roughly 1-2 minutes, plus about
  1 minute per 5 minutes of speech for a translation;
- that you will report progress as you go.

Windows that were transcribed before come back at once (results are kept); count them as no time.

## 2. Transcribe in short windows, with progress

- Call `transcribe` in windows of 30-60 s over the kept part, not the whole clip in one call. A whole-clip run
  puts quick back-and-forth between two people into one line, and its times drift.
- Let the windows overlap by about 2 s and drop the lines that come twice.
- **Progress:** after each window (or every few windows when they are short), write one line to the user before
  the next call: what is done and what is left, and the updated time left, e.g.
  `Transcribed 4:00 of 15:00 (5/16 windows), about 11 min left.`
  After the first slow window, estimate from how long it really took instead of the rule of thumb. Clip times
  (m:ss) in the message, not seconds.
- Also report when you move on to the next stage (checking words, fixing times, writing, translating), so
  the user always knows what is running.
- For a clip under about 2 minutes (2-3 windows), one line at the start and one at the end is enough.

## 3. Throw out what nobody said

Speech to text makes up text where there is no speech (quiet parts, music, noise). Drop:

- the same line three or more times in a row ("I'm not sure what you're doing" x18);
- a lone "Thank you", "you", "Bye", "Okay" at the end of a window or in a quiet stretch;
- sound tags such as `*Gunshot*`, `*BEEP*`, `*music*`: they are usually wrong;
- a word that only half matches the line before it (a fragment such as "Replied." right after "multiplied");
- lines in a stretch where the frames show nobody talking and no subtitle.

Keep the words as spoken: swearing stays, grammar is not polished.

## 4. Check doubtful words

- **Text burned into the picture** (subtitles at the bottom centre, often with the speaker's name) is the best
  reference for words, names and spelling: `frames` at the middle of the line, and read the text. Width 640
  usually reads fine; use 960-1280 only for small text, since big frames cost more. Hints, objectives and
  other on-screen labels are not speech; leave them out.
- A speaker who talks only as a distorted sound (it comes out as repeated nonsense tags) but has a subtitle
  on screen: use the on-screen line, and say so in the report.
- Doubtful lines: a name that changes between lines (Della / Donovan = Dylan), a word that does not fit the
  sentence or the scene ("time, time, time" while people jump into water = "dive, dive, dive"), a word that
  does not exist ("scoobotard" = "scuba tour").
- No text on screen: look at the scene in `frames`, compare with names used elsewhere in the clip, and
  `transcribe` just that line again with 5-10 s around it. If it is still unclear, keep the line and list it
  as unchecked; do not invent words.
- Every change goes into the report (old -> new, and why: on screen, scene, re-run).

## 5. Fix the timing

What speech to text gets wrong:

- **After a pause, a line starts where the previous line ended, not when the voice starts.** Its end is about
  right. So when a line lasts longer than its words need, move its start: need = characters / 15 + 0.3 s,
  start = end - need. Example: "How ya doin' up here?" came as 24.3-30.3 s; the voice starts at 29.1 s.
- Lines with round times or lengths (whole seconds, exactly 1.0, 2.0, 5.0 s) or longer than about 6 s have
  rough times.
- In noisy stretches times can be 2-6 s off in either direction, also after re-running the window.
- **On-screen subtitles give the real start**: the first frame where the line shows (frames every 0.5 s around
  it). Where they and the speech times disagree, the screen wins. Check every line that was moved, merged, long
  or near noise; spot-check the rest (one in five or so).

## 6. One sentence per subtitle

- Split every line at sentence ends (. ? !) and at speaker changes (a leading "- " from speech to text, or a
  new name in the on-screen subtitle, marks a new speaker). Two people never share one subtitle.
- Times inside a split line are shared by the number of characters.
- A one-word lead-in of the same speaker ("Okay.", "Stop!") stays with the sentence after it; a run of
  one-word shouts ("Now! Stop! STOP!") stays together.
- A sentence longer than about 6 s may be split in two at a comma.

## 7. Subtitle rules

- On screen about characters / 14 + 0.8 s: at least 0.7 s (1 s when there is room), at most 6 s.
- Never overlapping, at least 40 ms apart, in the spoken order.
- At most 2 lines of about 42 characters, broken at the space nearest the middle.
- SRT: numbered from 1, `HH:MM:SS,mmm --> HH:MM:SS,mmm`, UTF-8 without BOM, CRLF line ends.
- Times are the source video's. A trimmed or sped-up export has other times: say so, and for the export
  subtract the trim start (speed sections change the times after them).

## 8. Translation (when asked)

- Translate from the checked subtitles, one subtitle for one, with the same times, so both files stay in step.
- Translate by meaning and context (who speaks to whom, what happens in the scene), not word for word.
  Keep names, places and proper nouns of things as they are, with the target language's endings
  where it needs them (Hiss -> "Hiss-szel").
- Keep the tone: swearing stays swearing, shouts stay short, broken-off sentences stay broken off ("Do you--").
- Keep each line short enough for the timing rules in step 7; shorten the wording rather than the time.

## 9. Report

Say where the file(s) are, how many subtitles they have, and how long the job took. Then list: the corrected
lines (old -> new, why), the lines still unchecked, what was dropped as made up, and any translation choices
worth knowing (names kept, terms). Ask the user to play it with the video and name any spot that is out of
sync; fix those by finding the line's first frame on screen (steps 4 and 5) and keep the previous file as
`Clip.v2.srt` and so on.
