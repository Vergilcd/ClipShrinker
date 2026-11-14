"""Makes the short test clips used by the Phase 2 end-to-end checks (tests/media, not committed).

    ..\\Dev\\library\\python\\python.exe -I tools\\make_test_clips.py

Synthetic content only (ffmpeg testsrc2 + sine tones), encoded with NVENC because this ffmpeg build has no libx264.
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MEDIA = os.path.join(ROOT, "tests", "media")
FFMPEG = os.path.join(ROOT, "..", "Dev", "library", "ffmpeg.exe")

CLIPS = {
    # name: (seconds, w, h, rate, audio tones, codec args)
    "a_1080p60.mp4": (20, 1920, 1080, "60", [440], ["-c:v", "h264_nvenc", "-b:v", "12M"]),
    "b_ultrawide_2tracks.mov": (12, 3440, 1440, "30", [330, 660], ["-c:v", "hevc_nvenc", "-b:v", "16M"]),
    "c_720p_ntsc_noaudio.mp4": (8, 1280, 720, "30000/1001", [], ["-c:v", "h264_nvenc", "-b:v", "4M"]),
    "d_hdr10.mp4": (6, 1920, 1080, "30", [550], ["-c:v", "hevc_nvenc", "-profile:v", "main10", "-pix_fmt", "p010le",
                                                 "-b:v", "10M", "-color_primaries", "bt2020",
                                                 "-color_trc", "smpte2084", "-colorspace", "bt2020nc",
                                                 # NVENC ignores the flags above here: write the VUI tags directly
                                                 "-bsf:v", "hevc_metadata=colour_primaries=9:transfer_characteristics=16"
                                                           ":matrix_coefficients=9"]),
}


def S(**kw):
    """Project settings: the values that matter for a test, everything else from the app defaults."""
    base = {"size": "10", "bitrate": "", "res": "Auto", "codec": "HEVC (hevc_nvenc)", "crop": "16:9", "audio": "128k",
            "atrk": "Keep separate tracks", "mute_mic": False, "vhs": False, "hdr": True, "mode": "join",
            "trans": "Crossfade (gradient)", "tlen": "0.4", "slow_interp": "Frame blending"}
    base.update(kw)
    return base


def clip(name, **kw):
    d = {"path": os.path.join(MEDIA, name), "start": 0.0, "end": 9999, "preset": None, "sections": [], "zooms": []}
    d.update(kw)
    return d


PROJECTS = {
    "single_effects": ([clip("a_1080p60.mp4", start=2.0, end=18.0,
                             sections=[{"a": 4.0, "b": 8.0, "speed": 3.0}, {"a": 10.0, "b": 12.0, "speed": 0.5}],
                             zooms=[{"a": 13.0, "b": 16.0, "x": 0.2, "y": 0.2, "w": 0.5}])],
                       S(vhs=True, size="8")),
    "join_fade_hdr": ([clip("a_1080p60.mp4", end=6.0), clip("c_720p_ntsc_noaudio.mp4"), clip("d_hdr10.mp4")],
                      S(size="12")),
    "join_glitch_ultrawide": ([clip("b_ultrawide_2tracks.mov", start=1.0, end=7.0), clip("a_1080p60.mp4", end=5.0)],
                              S(bitrate="6", crop="21:9", trans="Glitch (digital)")),
    "join_hardcut_mix_mutemic": ([clip("b_ultrawide_2tracks.mov", end=5.0), clip("a_1080p60.mp4", end=4.0)],
                                 S(trans="Hard cut", atrk="Mix into one track", size="6", codec="H.264 (h264_nvenc)")),
    "join_mix_mutemic_on": ([clip("b_ultrawide_2tracks.mov", end=5.0), clip("a_1080p60.mp4", end=4.0)],
                            S(trans="Swipe left (push)", atrk="Mix into one track", mute_mic=True, size="6")),
    "separate_presets": ([clip("a_1080p60.mp4", end=6.0), clip("b_ultrawide_2tracks.mov", end=6.0, preset="Low")],
                         S(mode="separate", size="5")),
    "rife_slowmo": ([clip("a_1080p60.mp4", start=1.0, end=7.0, sections=[{"a": 3.0, "b": 4.0, "speed": 0.5}])],
                    S(slow_interp="RIFE AI (smoothest, needs a GPU)", size="6")),
    "hdr_keep": ([clip("d_hdr10.mp4")], S(hdr=False, size="4")),
}


def write_projects():
    d = os.path.join(MEDIA, "projects")
    os.makedirs(d, exist_ok=True)
    for name, (clips, st) in PROJECTS.items():
        with open(os.path.join(d, name + ".clipproj"), "w", encoding="utf-8") as f:
            json.dump({"app": "Clip Shrinker", "format": 1, "clips": clips, "active": 0, "settings": st}, f, indent=1)
    with open(os.path.join(d, "presets.json"), "w", encoding="utf-8") as f:
        json.dump({"presets": [{"name": "Low", "mbps": 3, "key": ""}]}, f, indent=1)
    print("projects:", ", ".join(PROJECTS))


def main():
    os.makedirs(MEDIA, exist_ok=True)
    write_projects()
    for name, (sec, w, h, rate, tones, vargs) in CLIPS.items():
        out = os.path.join(MEDIA, name)
        if os.path.exists(out):
            print("exists:", name)
            continue
        # This ffmpeg build has no lavfi device and no testsrc/sine: build the picture from color + noise + a moving
        # box, and the tones with aevalsrc, all inside -filter_complex.
        bw, bh = w // 6, h // 4
        g = [f"color=c=0x3060a0:s={w}x{h}:r={rate}:d={sec},noise=alls=25:allf=t,hue=H=2*PI*t/{sec}[bg]",
             f"color=c=0xf0c040:s={bw}x{bh}:r={rate}:d={sec}[box]",
             f"[bg][box]overlay=x='mod(t*{w // 4},{w - bw})':y='{h // 2 - bh // 2}+{h // 4}*sin(t*2)'[v]"]
        for k, f in enumerate(tones):
            g.append(f"aevalsrc='0.3*sin(2*PI*{f}*t)|0.3*sin(2*PI*{f}*t)':s=48000:d={sec}[a{k}]")
        cmd = [FFMPEG, "-y", "-v", "error", "-filter_complex", ";".join(g), "-map", "[v]"]
        cmd += sum((["-map", f"[a{k}]"] for k in range(len(tones))), [])
        cmd += vargs
        if tones:
            cmd += ["-c:a", "aac", "-b:a", "128k"]
        cmd += [out]
        r = subprocess.run(cmd, capture_output=True, text=True)
        if r.returncode != 0:
            sys.exit(f"{name}: {r.stderr.strip()}")
        print("made:", name, os.path.getsize(out), "bytes")


if __name__ == "__main__":
    main()
