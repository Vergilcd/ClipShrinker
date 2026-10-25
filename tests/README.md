# Tests

Headless tests run the real app under Xvfb with a Linux Python that has tkinter
(e.g. `uv python install 3.12`) and a Linux ffmpeg with lavfi.

    xvfb-run -a -s "-screen 0 1600x1000x24" python3.12 tests/legacy/test_engine.py

`tests/legacy/` holds the scripts written during development (some target old versions and may need small
fixes). `tests/legacy/gui/t32.py` has the harness head: it replaces `app.after` with a queue so thread
callbacks run, offers `pump(n)`, and fakes thumbnails (real ones don't render in the sandbox).
Make sample videos with e.g. `ffmpeg -f lavfi -i testsrc2=size=1920x1080:rate=60 -t 20 clip.mp4`.
