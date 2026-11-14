"""Phase 0 of the C# port: golden fixtures from the Python 1.0.8 logic.

Loads the frozen copy tools/reference/video_shrinker_1.0.8.py (never the live library/video_shrinker.py),
calls its pure functions and its Encoder with a fake file system, and writes what comes out to
tests/fixtures/*.json. The C# tests load those files and must produce the same results.

Run with the bundled Python, from the CSharp folder:
    ..\\Dev\\library\\python\\python.exe -I tools\\dump_fixtures.py

Nothing here runs ffmpeg: commands are captured, not executed. The output is deterministic, so running
the script again gives identical files (the "check" step compares a second run against the first).
"""
import copy
import faulthandler
import hashlib
import importlib.util
import json
import os
import sys
import types

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
REF = os.path.join(HERE, "reference", "video_shrinker_1.0.8.py")
OUT = os.path.join(ROOT, "tests", "fixtures")
FILES = os.path.join(OUT, "files")


# ------------------------------------------------------------------ loading the reference
def load_reference():
    spec = importlib.util.spec_from_file_location("video_shrinker_ref", REF)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    # Import side effect: the module opens clipshrinker.log next to itself. Close and remove it.
    faulthandler.disable()
    if m._LOG:
        m._LOG.close()
        m._LOG = None
    try:
        os.remove(os.path.join(os.path.dirname(REF), "clipshrinker.log"))
    except OSError:
        pass
    m.FFMPEG, m.FFPROBE = "ffmpeg", "ffprobe"   # tool paths are machine-specific: normalise them
    m.RIFE_DIR = "<rife-not-installed>"         # rife_exe() -> None, so exports take the "not installed" path
    return m


m = load_reference()
GLOBAL_NAMES = ("AUD", "VHS", "SLOW", "ZOOM")
DEFAULTS = {k: copy.deepcopy(getattr(m, k)) for k in GLOBAL_NAMES}


def reset_globals():
    for k in GLOBAL_NAMES:
        d = getattr(m, k)
        d.clear()
        d.update(copy.deepcopy(DEFAULTS[k]))


def globals_state():
    """The module-level effect settings that the filter builders read. Tests must set these first."""
    return {k: copy.deepcopy(getattr(m, k)) for k in GLOBAL_NAMES}


def apply_effect_settings(st):
    """Same mapping as Encoder._encode (reference lines 1638-1651)."""
    m.VHS["on"] = bool(st.get("vhs", False))
    m.AUD["fast"] = m.aud_key(st.get("fast_audio", m.FAST_AUDIO[0]))
    m.AUD["slow"] = m.aud_key(st.get("slow_audio", m.SLOW_AUDIO[0]))
    m.AUD["deep"] = st.get("slow_pitch", m.SLOW_PITCH[0]) == m.SLOW_PITCH[0]
    m.AUD["high"] = st.get("fast_pitch", m.FAST_PITCH[1]) == m.FAST_PITCH[0]
    m.AUD["fast_vol"] = int(st.get("fast_vol", 70))
    m.AUD["slow_vol"] = int(st.get("slow_vol", 150))
    m.ZOOM["motion"] = st.get("zoom_fx", m.ZOOM_FX[0]) == m.ZOOM_FX[0]
    m.ZOOM["len"] = float(st.get("zoom_len", 0.4))
    m.ZOOM["sound"] = st.get("zoom_snd", m.ZOOM_AUDIO[0]) == m.ZOOM_AUDIO[0]
    m.ZOOM["vol"] = int(st.get("zoom_vol", 80))
    mode = m.interp_key(st.get("slow_interp", m.SLOW_INTERP[1]))
    m.SLOW["blend"] = mode != "repeat"
    m.SLOW["rife"] = mode == "rife"


def jsonable(x):
    """Tuples -> lists, recursively (so the fixtures are plain JSON)."""
    if isinstance(x, (list, tuple)):
        return [jsonable(v) for v in x]
    if isinstance(x, dict):
        return {str(k): jsonable(v) for k, v in x.items()}
    if isinstance(x, bytes):
        return x.decode("ascii")
    if isinstance(x, (set, frozenset)):
        return sorted(jsonable(v) for v in x)
    if isinstance(x, float) and (x != x or x in (float("inf"), float("-inf"))):
        return {"float": repr(x)}                  # NaN / inf are not valid JSON numbers
    return x


def call(fn, *args, **kw):
    """{"result": ...} or {"error": "<ExceptionType>: message"}."""
    try:
        return {"result": jsonable(fn(*args, **kw))}
    except Exception as e:
        return {"error": f"{type(e).__name__}: {e}"}


# ------------------------------------------------------------------ clip helper
def clip(path="C:\\clips\\a.mp4", start=0.0, end=20.0, w=1920, h=1080, fps=60.0, naudio=1, bitrate=8_000_000.0,
         trc=None, prim="bt709", space="bt709", sections=(), zooms=()):
    """A clip dict exactly as ClipRow.clip() builds it (reference line 3223), from timeline-level input.
    sections: [{"a","b","speed"}]; zooms: [{"a","b","x","y","w"}] (source seconds, picture fractions)."""
    secs = sorted(sections, key=lambda x: x["a"])
    vis = [s for s in secs if min(s["b"], end) - max(s["a"], start) >= 0.05]           # ClipRow.vis()
    zs = []
    for z in sorted(zooms, key=lambda z: z["a"]):                                    # ClipRow.vis_z()
        a, b = max(z["a"], start), min(z["b"], end)
        if b - a >= 0.05:
            zs.append((a, b, z["x"], z["y"], z["w"]))
    return {"path": path, "start": start, "end": end, "w": w, "h": h, "fps": fps, "audio": naudio > 0,
            "naudio": naudio, "bitrate": bitrate, "trc": trc, "prim": prim, "space": space,
            "pieces": m.build_pieces(start, end, vis) if vis else None, "zooms": zs}


def row_input(**kw):
    """The timeline-level description of a clip (what the C# side builds its model from)."""
    keys = ("path", "start", "end", "w", "h", "fps", "naudio", "bitrate", "trc", "prim", "space", "sections", "zooms")
    return {k: jsonable(kw[k]) for k in keys if k in kw}


def clip_case(**kw):
    return {"row": row_input(**kw), "clip": jsonable(clip(**kw))}


HDR = dict(trc="smpte2084", prim="bt2020", space="bt2020nc")
HLG = dict(trc="arib-std-b67", prim="bt2020", space="bt2020nc")

BASE_ST = {
    "vhs": False, "fast_audio": "VHS effect with sound", "slow_audio": "Slow-mo effect with sound",
    "slow_pitch": "Slows down with the video (deep)", "fast_pitch": "Stays the same (compressed)",
    "fast_vol": 70, "slow_vol": 150, "zoom_fx": "Zoom motion", "zoom_snd": "Whoosh sound", "zoom_len": "0.4",
    "zoom_vol": 80, "slow_interp": "Frame blending",
}


def st_with(**kw):
    d = dict(BASE_ST)
    d.update(kw)
    return d


# ------------------------------------------------------------------ helpers.json
def dump_helpers():
    out = {}

    def table(name, fn, arglists):
        out[name] = [dict(args=jsonable(a), **call(fn, *a)) for a in arglists]

    table("fmt_elapsed", m.fmt_elapsed, [(x,) for x in (0, -3, 0.4, 0.5, 1, 1.5, 2.5, 59.6, 60, 61, 233, 3600,
                                                         3601, 3725, 7322.5, 86399)])
    table("fmt_time", m.fmt_time, [(x,) for x in (0, -1, 5.123, 9.995, 59.999, 60, 83.5, 3599.996, 3600,
                                                  3725.5, 36000.25)])
    table("parse_clock", m.parse_clock, [(x,) for x in ("83.5", "1:23.5", "1:02:03", "1,5", "1;30", "", "  ",
                                                        "abc", "1:2:3:4", "-1", "  12  ", "1:-2", "0:00.25",
                                                        "90:00", "1e2", "nan", "inf")])
    table("fmt_tick", m.fmt_tick, [(x,) for x in (0, 0.5, 1.5, 2.5, 59.5, 60, 3599.5, 3600, 3661, 7322)])
    table("parse_fps", m.parse_fps, [(x,) for x in ("60/1", "30000/1001", "24000/1001", "60000/1001", "0/0",
                                                    "25", "abc", "59.94", "", "1/0")])
    table("even", m.even, [(x,) for x in (0, 1, 2, 3, 1079.9, 1081, -5, 1440.0)])
    sizes = [(1920, 1080), (3440, 1440), (2560, 1080), (2560, 1440), (1080, 1920), (1280, 1024), (3840, 2160),
             (5120, 1440), (1440, 1080), (1366, 768), (1921, 1081)]
    table("crop_size", m.crop_size, [(w, h, r) for (w, h) in sizes for r in m.ASPECTS.values()])
    table("choose_scale", m.choose_scale,
          [(mode, w, h, fps, bps) for mode in ("Auto", "Original", "100%", "75%", "50%")
           for (w, h, fps) in ((1920, 1080, 60.0), (2560, 1440, 144.0), (1280, 720, 30.0))
           for bps in (500_000.0, 3_000_000.0, 6_220_800.0, 25_000_000.0)])
    table("tonemap_chain", m.tonemap_chain, [("smpte2084",), ("arib-std-b67",)])
    table("video_codec_args", m.video_codec_args, [("h264_nvenc", 8e6), ("hevc_nvenc", 15000.0),
                                                   ("h264_nvenc", 19999.0), ("hevc_nvenc", 2_500_000.7),
                                                   ("h264_nvenc", 123456789.0)])
    table("atempo_chain", m.atempo_chain, [(x,) for x in (0.1, 0.2, 0.25, 0.3, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0,
                                                          4.0, 10.0, 33.3, 100.0)])
    table("clamp_speed", m.clamp_speed, [(x,) for x in (0.01, 0.1, 1.0, 50.0, 100.0, 1000.0)])
    table("aud_key", m.aud_key, [(x,) for x in m.FAST_AUDIO + m.SLOW_AUDIO + ["other"]])
    table("interp_key", m.interp_key, [(x,) for x in m.SLOW_INTERP + ["other"]])
    table("codec_key", m.codec_key, [(x,) for x in m.CODECS + ["other"]])
    table("codec_short", m.codec_short, [(x,) for x in m.CODECS + ["other"]])
    table("xfade_mode", m.xfade_mode, [(x,) for x in m.TRANSITIONS.values()])
    table("chord_label", m.chord_label, [(x,) for x in ("s", "1", "ctrl+shift+s", "f2", "f12", "exclam",
                                                        "alt+prior", "next", "space", "return", "home",
                                                        "ctrl+alt+shift+bracketleft", "escape", "insert")])
    table("clean_suffix", m.clean_suffix, [(x,) for x in ('a<b>c', '  ._name_. ', 'x\x01y', '', None,
                                                          'clip:final?', '_v2_', 'ok name')])
    # Fraction(fps).limit_denominator(1001): used for every fps= / framerate= filter. C# must match exactly.
    fps_values = [60.0, 59.94, 30000 / 1001, 60000 / 1001, 24000 / 1001, 29.97, 23.976, 25.0, 24.0, 144.0,
                  120.0, 47.952, 59.940059940059939, 165.0, 0.5, 12.5, 239.76]
    from fractions import Fraction
    out["fraction_limit_denominator_1001"] = [
        {"args": [x], "result": [Fraction(x).limit_denominator(1001).numerator,
                                 Fraction(x).limit_denominator(1001).denominator]} for x in fps_values]
    # Python float formatting used in the filter strings ({x:.3f}, {x:g}). C# formatting must agree.
    fmt_values = [0.0, 0.0005, 0.0015, 0.0025, 1.0005, 2.675, 1 / 3, 2 / 3, 59.94, 30000 / 1001, 1e-7, 123456.789,
                  -0.0004, 0.4, 1.2345]
    out["python_format"] = [{"value": v, ".3f": f"{v:.3f}", ".4f": f"{v:.4f}", ".5f": f"{v:.5f}",
                             ".2f": f"{v:.2f}", ".0f": f"{v:.0f}", "g": f"{v:g}", ".6f": f"{v:.6f}"}
                            for v in fmt_values]
    out["clip_naudio"] = [dict(args=[c], **call(m.clip_naudio, c)) for c in
                          ({"naudio": 2}, {"audio": True}, {"audio": False}, {}, {"naudio": 0, "audio": True})]
    track_sets = [[1], [0], [2], [2, 1], [0, 0], [4, 1, 0], [3]]
    out["out_tracks"] = [dict(args=[ns, mix], **call(m.out_tracks, [{"naudio": n} for n in ns], mix))
                         for ns in track_sets for mix in (False, True)]
    color_cases = [[{}], [HDR], [HLG], [dict(trc="smpte2084", prim="unknown", space="unknown")],
                   [{}, HDR], [dict(trc="arib-std-b67")]]
    out["color_tags"] = [dict(args=[jsonable(c), tm], **call(m.Encoder._color_tags, [dict(x) for x in c], tm))
                         for c in color_cases for tm in (False, True)]
    vf_cases = []
    for (w, h) in ((1920, 1080), (3440, 1440), (1080, 1920), (2560, 1080)):
        for ratio in (None, 16 / 9, 21 / 9, 4 / 3):
            for trc in (None, "smpte2084"):
                for tm in (False, True):
                    c = {"w": w, "h": h, "trc": trc}
                    vf_cases.append(dict(args=[c, 1920, 1080, ratio, tm], **call(m.clip_vfilters, c, 1920, 1080, ratio, tm)))
    out["clip_vfilters"] = vf_cases
    return out


# ------------------------------------------------------------------ pieces.json
PIECE_CASES = [
    ("no sections", 0.0, 10.0, []),
    ("one fast section inside", 0.0, 10.0, [{"a": 2.0, "b": 4.0, "speed": 2.0}]),
    ("section at the start", 1.0, 10.0, [{"a": 1.0, "b": 3.0, "speed": 4.0}]),
    ("section at the end", 0.0, 10.0, [{"a": 8.0, "b": 10.0, "speed": 3.0}]),
    ("section covers everything", 2.0, 6.0, [{"a": 0.0, "b": 9.0, "speed": 2.5}]),
    ("section before the trim", 5.0, 10.0, [{"a": 0.0, "b": 4.0, "speed": 2.0}]),
    ("section after the trim", 0.0, 5.0, [{"a": 6.0, "b": 8.0, "speed": 2.0}]),
    ("section crosses the start", 3.0, 10.0, [{"a": 1.0, "b": 5.0, "speed": 2.0}]),
    ("section crosses the end", 0.0, 7.0, [{"a": 5.0, "b": 9.0, "speed": 0.5}]),
    ("slow and fast", 0.0, 20.0, [{"a": 2.0, "b": 5.0, "speed": 0.5}, {"a": 10.0, "b": 15.0, "speed": 4.0}]),
    ("unsorted input", 0.0, 20.0, [{"a": 10.0, "b": 15.0, "speed": 4.0}, {"a": 2.0, "b": 5.0, "speed": 0.5}]),
    ("touching sections", 0.0, 10.0, [{"a": 2.0, "b": 4.0, "speed": 2.0}, {"a": 4.0, "b": 6.0, "speed": 3.0}]),
    ("overlapping sections", 0.0, 10.0, [{"a": 2.0, "b": 5.0, "speed": 2.0}, {"a": 4.0, "b": 7.0, "speed": 3.0}]),
    ("tiny section is dropped", 0.0, 10.0, [{"a": 2.0, "b": 2.015, "speed": 2.0}]),
    ("gap below 1 ms is merged", 0.0, 10.0, [{"a": 0.0005, "b": 3.0, "speed": 2.0}]),
    ("ends 0.5 ms before the end", 0.0, 10.0, [{"a": 4.0, "b": 9.9995, "speed": 2.0}]),
    ("speed 1 section", 0.0, 10.0, [{"a": 3.0, "b": 6.0, "speed": 1.0}]),
    ("extreme speeds", 0.0, 60.0, [{"a": 5.0, "b": 50.0, "speed": 100.0}, {"a": 50.0, "b": 51.0, "speed": 0.1}]),
    ("five sections", 1.5, 31.5, [{"a": 2.0, "b": 3.0, "speed": 2.0}, {"a": 5.0, "b": 8.0, "speed": 0.25},
                                  {"a": 10.0, "b": 12.5, "speed": 6.0}, {"a": 15.0, "b": 20.0, "speed": 1.5},
                                  {"a": 25.0, "b": 31.0, "speed": 0.75}]),
    ("fractional times", 0.333, 7.777, [{"a": 1.111, "b": 2.222, "speed": 1.7}]),
]

ZOOM_CASES = [
    ("one zoom, plain", [(2.0, 5.0, 0.25, 0.25, 0.5)], None),
    ("zoom outside", [(12.0, 15.0, 0.1, 0.1, 0.3)], None),
    ("zoom too short", [(2.0, 2.04, 0.1, 0.1, 0.3)], None),
    ("two zooms", [(1.0, 3.0, 0.0, 0.0, 0.5), (6.0, 9.0, 0.5, 0.5, 0.5)], None),
    ("zoom inside a fast section", [(3.0, 5.0, 0.2, 0.3, 0.4)], [{"a": 2.0, "b": 6.0, "speed": 4.0}]),
    ("zoom across a slow section", [(1.0, 4.0, 0.4, 0.4, 0.2)], [{"a": 2.0, "b": 3.0, "speed": 0.5}]),
    ("zoom at the edge", [(0.0, 10.0, 0.9, 0.9, 0.1)], None),
    ("very small zoom area", [(2.0, 4.0, 0.5, 0.5, 0.01)], None),
]


def dump_pieces():
    out = {"build_pieces": [], "zoom_times": [], "zoom_spans": []}
    for name, start, end, secs in PIECE_CASES:
        pieces = m.build_pieces(start, end, secs)
        total = m.pieces_duration(pieces)
        probes_out = [round(total * k / 8, 6) for k in range(9)] + [total + 1.0, -0.5]
        probes_src = [start - 1.0, start] + [round(start + (end - start) * k / 7, 6) for k in range(1, 7)] + [end, end + 1.0]
        out["build_pieces"].append({
            "name": name, "start": start, "end": end, "sections": secs, "pieces": jsonable(pieces),
            "duration": total, "is_plain": m.is_plain(pieces),
            "out_to_src": [[t, m.out_to_src(pieces, t)] for t in probes_out],
            "src_to_out": [[t, m.src_to_out(pieces, t)] for t in probes_src],
        })
    out["is_plain_extra"] = [{"pieces": [], "result": m.is_plain([])},
                             {"pieces": None, "result": m.is_plain(None)},
                             {"pieces": [[0, 5, 1.0000001]], "result": m.is_plain([(0, 5, 1.0000001)])}]
    for name, zooms, secs in ZOOM_CASES:
        pieces = m.build_pieces(0.0, 10.0, secs or [])
        out["zoom_times"].append({"name": name, "zooms": jsonable(zooms), "pieces": jsonable(pieces),
                                  "result": jsonable(m.zoom_times(zooms, pieces))})
        for (cw0, ch0, W, H) in ((1920, 1080, 1920, 1080), (2520, 1080, 1920, 1080), (1080, 1920, 1280, 720),
                                 (1440, 1080, 1920, 1080)):
            out["zoom_spans"].append({"name": name, "zooms": jsonable(zooms), "pieces": jsonable(pieces),
                                      "cw0": cw0, "ch0": ch0, "W": W, "H": H,
                                      "result": jsonable(m.zoom_spans(zooms, pieces, cw0, ch0, W, H))})
    out["zoom_times_empty"] = [{"zooms": None, "pieces": [[0, 1, 1.0]], "result": m.zoom_times(None, [(0, 1, 1.0)])},
                               {"zooms": [[0, 1, 0, 0, 1]], "pieces": [], "result": m.zoom_times([(0, 1, 0, 0, 1)], [])}]
    return out


# ------------------------------------------------------------------ filters.json
def dump_filters():
    out = {k: [] for k in ("glitch_parts", "vhs_video_parts", "vhs_audio_parts", "slow_audio_parts",
                           "zoom_vfilter", "zoom_whoosh_parts", "seg_video", "seg_audio")}
    for args in (("vx1g", "vx1", 9.6, 0.4, 1920, 1080, "gl1", 38), ("g0", "g1", 0.4, 0.8, 240, 136, "pg", 7),
                 ("a", "b", 30.0, 1.0, 1280, 720, "gl2", 69), ("a", "b", 5.0, 0.2, 110, 50, "t", 0),
                 ("vx3g", "vx3", 100.123, 0.45, 2560, 1440, "gl3", 100)):
        src, o, a, T, W, H, tag, seed = args
        out["glitch_parts"].append({"args": list(args), "result": m.glitch_parts(src, o, a, T, W, H, tag, seed=seed)})
    for D in (0.1, 0.5, 0.7, 3.0, 12.345):
        for size in (None, (1920, 1080), (1280, 720), (240, 136)):
            out["vhs_video_parts"].append({"args": ["c0p1_r", "c0p1", D, jsonable(size)],
                                           "result": m.vhs_video_parts("c0p1_r", "c0p1", D, size)})
    for D in (0.15, 0.5, 0.9, 1.2, 4.0, 20.0):
        for rate in (48000, 44100):
            out["vhs_audio_parts"].append({"args": ["d_m", "d_o", D, rate], "result": m.vhs_audio_parts("d_m", "d_o", D, rate)})
            out["slow_audio_parts"].append({"args": ["d_m", "d_o", D, rate], "result": m.slow_audio_parts("d_m", "d_o", D, rate)})
    spans_list = [
        [(1.0, 3.0, 0.25, 0.25, 0.5)],
        [(1.0, 1.15, 0.1, 0.2, 0.3)],
        [(0.0, 2.0, 0.0, 0.0, 0.5), (4.0, 9.0, 0.45, 0.45, 0.1)],
        [(2.5, 7.25, 0.123456, 0.654321, 0.333333)],
    ]
    for spans in spans_list:
        for motion, length in ((True, 0.4), (True, 1.5), (True, 0.01), (False, 0.4)):
            reset_globals()
            m.ZOOM["motion"], m.ZOOM["len"] = motion, length
            for W, H in ((1920, 1080), (1280, 720)):
                out["zoom_vfilter"].append({"globals": globals_state(), "args": [jsonable(spans), W, H],
                                            "result": m.zoom_vfilter(spans, W, H)})
    times_list = [[(1.0, 3.0, None)], [(0.0, 0.4, None)], [(2.0, 5.0, None), (8.0, 12.0, None)], []]
    for times in times_list:
        for motion, length, vol in ((True, 0.4, 80), (True, 1.2, 200), (True, 0.1, 50), (False, 0.4, 80), (True, 0.4, 0)):
            for rate in (48000, 44100):
                reset_globals()
                m.ZOOM.update(motion=motion, len=length, vol=vol)
                out["zoom_whoosh_parts"].append({"globals": globals_state(),
                                                 "args": ["lab", "out", jsonable(times), rate, "zw0"],
                                                 "result": m.zoom_whoosh_parts("lab", "out", times, rate, "zw0")})
    seg_pieces = [
        [(0.0, 10.0, 1.0)],
        [(2.0, 4.0, 2.0)],
        [(2.0, 4.0, 0.5)],
        [(0.0, 2.0, 1.0), (2.0, 4.0, 3.0), (4.0, 10.0, 1.0)],
        [(1.0, 2.0, 0.5), (2.0, 2.2, 4.0), (2.2, 9.0, 1.0)],
        [(5.0, 6.0, 0.25), (6.0, 30.0, 8.0), (30.0, 30.1, 2.0)],
    ]
    video_cfgs = [dict(vhs=False, blend=True), dict(vhs=True, blend=True), dict(vhs=False, blend=False),
                  dict(vhs=True, blend=False)]
    for pieces in seg_pieces:
        t_base = pieces[0][0]
        for cfg in video_cfgs:
            for size, fps in (((1920, 1080), 60.0), ((1280, 720), 30000 / 1001), (None, None)):
                reset_globals()
                m.VHS["on"], m.SLOW["blend"] = cfg["vhs"], cfg["blend"]
                out["seg_video"].append({"globals": globals_state(),
                                         "args": ["b0", jsonable(pieces), t_base, "c0", "m0", jsonable(size), fps, None],
                                         "result": m.seg_video("b0", pieces, t_base, "c0", "m0", size, fps)})
        # with RIFE frames for every slow piece: input index, rendered size (same and different from size)
        rife = {j: (3 + j, 1920, 1080) for j, p in enumerate(pieces) if p[2] < 1.0}
        if rife:
            for size in ((1920, 1080), (1280, 720)):
                reset_globals()
                out["seg_video"].append({"globals": globals_state(),
                                         "args": ["b0", jsonable(pieces), t_base, "c0", "m0", list(size), 60.0, jsonable(rife)],
                                         "result": m.seg_video("b0", pieces, t_base, "c0", "m0", size, 60.0, rife)})
    audio_cfgs = [
        dict(fast="fx", slow="fx", deep=True, high=False, fast_vol=70, slow_vol=150),
        dict(fast="fx", slow="fx", deep=True, high=False, fast_vol=100, slow_vol=100),
        dict(fast="normal", slow="normal", deep=False, high=True, fast_vol=100, slow_vol=100),
        dict(fast="normal", slow="normal", deep=True, high=False, fast_vol=0, slow_vol=200),
        dict(fast="mute", slow="mute", deep=True, high=True, fast_vol=70, slow_vol=150),
        dict(fast="fx", slow="normal", deep=False, high=True, fast_vol=55, slow_vol=100),
    ]
    for pieces in seg_pieces:
        t_base = pieces[0][0]
        for cfg in audio_cfgs:
            for rate in (48000, 44100):
                reset_globals()
                m.AUD.update(cfg)
                out["seg_audio"].append({"globals": globals_state(),
                                         "args": ["ab0", jsonable(pieces), t_base, "d0_0", "a0_0", True, rate],
                                         "result": m.seg_audio("ab0", pieces, t_base, "d0_0", "a0_0", True, rate)})
    reset_globals()
    return out


# ------------------------------------------------------------------ graphs.json
def graph_cases():
    A, B, C, D = "C:\\clips\\a.mp4", "C:\\clips\\b.mkv", "D:\\Videos\\c (1).mov", "C:\\clips\\d.webm"
    fast = [{"a": 4.0, "b": 8.0, "speed": 2.0}]
    mixed = [{"a": 2.0, "b": 5.0, "speed": 4.0}, {"a": 9.0, "b": 11.0, "speed": 0.5}]
    zoom1 = [{"a": 3.0, "b": 7.0, "x": 0.25, "y": 0.2, "w": 0.4}]
    S = st_with
    cases = [
        ("single plain 1080p60", [dict(path=A)], 1920, 1080, None, None, 0, S(), {}),
        ("single crop 3440x1440 to 21:9", [dict(path=A, w=3440, h=1440)], 2520, 1080, 21 / 9, None, 0, S(), {}),
        ("single ultrawide to 16:9", [dict(path=A, w=3440, h=1440)], 2560, 1440, 16 / 9, None, 0, S(), {}),
        ("single vertical padded", [dict(path=A, w=1080, h=1920)], 1080, 1920, 16 / 9, None, 0, S(), {}),
        ("single trimmed plain", [dict(path=A, start=3.25, end=17.5)], 1920, 1080, None, None, 0, S(), {}),
        ("fast 2x, fx audio, no vhs", [dict(path=A, sections=fast)], 1920, 1080, None, None, 0, S(), {}),
        ("fast 2x with vhs", [dict(path=A, sections=fast)], 1920, 1080, None, None, 0, S(vhs=True), {}),
        ("fast + slow, deep pitch, blend", [dict(path=A, sections=mixed)], 1920, 1080, None, None, 0, S(), {}),
        ("slow, repeat frames", [dict(path=A, sections=mixed)], 1920, 1080, None, None, 0,
         S(slow_interp="Repeat frames"), {}),
        ("slow with RIFE frames", [dict(path=A, sections=mixed)], 1920, 1080, None, None, 0,
         S(slow_interp="RIFE AI (smoothest, needs a GPU)"), {"rife": {0: {2: (1, 1920, 1080)}}}),
        ("slow with RIFE frames, other size", [dict(path=A, sections=mixed)], 1280, 720, None, None, 0,
         S(slow_interp="RIFE AI (smoothest, needs a GPU)"), {"rife": {0: {2: (1, 1920, 1080)}}}),
        ("fast normal sound, high pitch", [dict(path=A, sections=mixed)], 1920, 1080, None, None, 0,
         S(fast_audio="Normal sped-up sound", fast_pitch="Speeds up with the video (high)"), {}),
        ("fast and slow muted", [dict(path=A, sections=mixed)], 1920, 1080, None, None, 0,
         S(fast_audio="Mute", slow_audio="Mute"), {}),
        ("slow normal, stretched pitch", [dict(path=A, sections=mixed)], 1920, 1080, None, None, 0,
         S(slow_audio="Normal slow-mo sound", slow_pitch="Stays the same (stretched)"), {}),
        ("effect volumes 100/100", [dict(path=A, sections=mixed)], 1920, 1080, None, None, 0,
         S(fast_vol=100, slow_vol=100), {}),
        ("effect volumes 0/200", [dict(path=A, sections=mixed)], 1920, 1080, None, None, 0,
         S(fast_vol=0, slow_vol=200), {}),
        ("zoom, motion, whoosh", [dict(path=A, zooms=zoom1)], 1920, 1080, None, None, 0, S(), {}),
        ("zoom, instant, mute", [dict(path=A, zooms=zoom1)], 1920, 1080, None, None, 0,
         S(zoom_fx="No effect (instant)", zoom_snd="Mute"), {}),
        ("zoom + speed sections", [dict(path=A, sections=mixed, zooms=zoom1)], 1920, 1080, None, None, 0,
         S(vhs=True), {}),
        ("zoom on cropped clip", [dict(path=A, w=3440, h=1440, zooms=zoom1)], 1920, 1080, 16 / 9, None, 0, S(), {}),
        ("two tracks separate", [dict(path=A, naudio=2)], 1920, 1080, None, None, 0, S(), {}),
        ("two tracks mixed", [dict(path=A, naudio=2)], 1920, 1080, None, None, 0, S(), {"mix": True}),
        ("two tracks mixed + sections", [dict(path=A, naudio=2, sections=fast)], 1920, 1080, None, None, 0, S(),
         {"mix": True}),
        ("no audio", [dict(path=A, naudio=0)], 1920, 1080, None, None, 0, S(), {}),
        ("no audio + sections", [dict(path=A, naudio=0, sections=mixed)], 1920, 1080, None, None, 0, S(), {}),
        ("HDR tonemap on", [dict(path=A, w=3840, h=2160, **HDR)], 1920, 1080, None, None, 0, S(), {"tonemap": True}),
        ("HDR tonemap off", [dict(path=A, w=3840, h=2160, **HDR)], 1920, 1080, None, None, 0, S(), {}),
        ("HLG tonemap + sections", [dict(path=A, **HLG, sections=fast)], 1920, 1080, None, None, 0, S(),
         {"tonemap": True}),
        ("join 2 hard cut", [dict(path=A), dict(path=B, start=1.0, end=9.0)], 1920, 1080, None, None, 0, S(), {}),
        ("join 3 crossfade", [dict(path=A), dict(path=B, end=12.0), dict(path=C, start=2.0, end=8.5)], 1920, 1080,
         None, "fade", 0.4, S(), {}),
        ("join 3 glitch", [dict(path=A), dict(path=B, end=12.0), dict(path=C, start=2.0, end=8.5)], 1920, 1080,
         None, "glitch", 0.4, S(), {}),
        ("join audio 2 + 0 separate", [dict(path=A, naudio=2), dict(path=B, naudio=0)], 1920, 1080, None, None, 0,
         S(), {}),
        ("join audio 2 + 1 mixed, fadeblack", [dict(path=A, naudio=2), dict(path=B, naudio=1)], 1920, 1080, None,
         "fadeblack", 0.5, S(), {"mix": True}),
        ("join with sections and zooms", [dict(path=A, sections=mixed, zooms=zoom1), dict(path=B, sections=fast)],
         1920, 1080, None, "smoothright", 0.4, S(vhs=True), {}),
        ("join 4 mixed sizes, slideleft", [dict(path=A), dict(path=B, w=1280, h=720, fps=30.0),
                                           dict(path=C, w=3440, h=1440), dict(path=D, w=1080, h=1920, fps=24.0)],
         1920, 1080, 16 / 9, "slideleft", 0.3, S(), {}),
        ("join 29.97 + 59.94", [dict(path=A, fps=30000 / 1001), dict(path=B, fps=60000 / 1001)], 1920, 1080, None,
         "circleopen", 0.4, S(), {}),
        ("very short pieces", [dict(path=A, sections=[{"a": 2.0, "b": 2.2, "speed": 2.0},
                                                      {"a": 5.0, "b": 5.06, "speed": 0.5}])], 1920, 1080, None, None,
         0, S(vhs=True), {}),
        ("five sections", [dict(path=A, end=40.0, sections=[{"a": 2.0, "b": 3.0, "speed": 2.0},
                                                             {"a": 5.0, "b": 8.0, "speed": 0.25},
                                                             {"a": 10.0, "b": 12.5, "speed": 6.0},
                                                             {"a": 15.0, "b": 20.0, "speed": 1.5},
                                                             {"a": 25.0, "b": 31.0, "speed": 0.75}])],
         1920, 1080, None, None, 0, S(vhs=True), {}),
        ("four audio tracks", [dict(path=A, naudio=4, sections=fast)], 1920, 1080, None, None, 0, S(), {}),
        ("join 2 with HDR + SDR, tonemap", [dict(path=A, **HDR), dict(path=B)], 1920, 1080, None, "distance", 0.4,
         S(), {"tonemap": True}),
    ]
    return cases


def dump_graphs():
    out = []
    for name, rows, W, H, ratio, xfade, T, st, extra in graph_cases():
        reset_globals()
        apply_effect_settings(st)
        cases = [clip_case(**r) for r in rows]
        clips = [copy.deepcopy(c["clip"]) for c in cases]
        for c in clips:                               # clip dicts use tuples in Python; lists behave the same
            if c["pieces"]:
                c["pieces"] = [tuple(p) for p in c["pieces"]]
            c["zooms"] = [tuple(z) for z in c["zooms"]]
        fps = max(c["fps"] for c in clips)
        rife = extra.get("rife")
        res = call(m.build_graph, clips, W, H, ratio, xfade, T, fps, extra.get("tonemap", False), True,
                   extra.get("mix", False), rife)
        entry = {"name": name, "settings": st, "globals": globals_state(), "clips": cases,
                 "args": {"W": W, "H": H, "ratio": ratio, "xfade": xfade, "T": T, "fps": fps,
                          "tonemap": extra.get("tonemap", False), "mute_fast": True, "mix": extra.get("mix", False),
                          "rife": jsonable(rife)}}
        entry.update(res)
        if "result" in entry:
            g, vl, al = entry.pop("result")
            entry["result"] = {"graph": g, "graph_parts": g.split(";"), "video_label": vl, "audio_labels": al}
        out.append(entry)
    reset_globals()
    return out


# ------------------------------------------------------------------ preview.json
class Capture:
    """Replaces run_capture / subprocess.Popen inside the reference while a function runs."""

    def __init__(self):
        self.cmds = []

    def __enter__(self):
        self._rc, self._popen = m.run_capture, m.subprocess.Popen
        cap = self

        def fake_run_capture(cmd, binary=False):
            cap.cmds.append(list(cmd))
            return types.SimpleNamespace(returncode=1, stdout=b"" if binary else "", stderr="")

        def fake_popen(cmd, *a, **kw):
            cap.cmds.append(list(cmd))
            raise OSError("captured")              # grab_frames then falls back to grab_frame per time

        m.run_capture = fake_run_capture
        m.subprocess.Popen = fake_popen
        return self

    def __exit__(self, *exc):
        m.run_capture, m.subprocess.Popen = self._rc, self._popen
        return False


def dump_preview():
    out = {"play_command": [], "preview_audio_cmd": [], "effect_sample_cmd": [], "grab_frame": [],
           "grab_frames": [], "transition_preview": []}
    P = "C:\\clips\\a b.mp4"
    play_pieces = [None, [(5.0, 15.0, 1.0)], [(5.0, 7.0, 1.0), (7.0, 9.0, 4.0), (9.0, 15.0, 1.0)],
                   [(5.0, 6.0, 0.5), (6.0, 15.0, 1.0)]]
    zoom_play = [[], [(6.0, 8.0, 0.25, 0.25, 0.5)]]
    for pieces in play_pieces:
        for zp in zoom_play:
            for hwdec in (0, 1, 2):
                for crop, trc in ((None, None), ((2520, 1440), None), (None, "smpte2084")):
                    for vhs in (False, True):
                        if vhs and not pieces:
                            continue
                        reset_globals()
                        m.ZOOM["play"] = list(zp)
                        m.VHS["on"] = vhs
                        args = [P, 5.0, 10.0, jsonable(crop), 1280, 720, 30, hwdec, trc, jsonable(pieces)]
                        out["play_command"].append({"globals": globals_state(), "args": args,
                                                    **call(m.play_command, P, 5.0, 10.0, crop, 1280, 720, 30, hwdec,
                                                           trc, pieces)})
    for pieces in play_pieces:
        for zp in zoom_play:
            for naudio in (0, 1, 2, 3):
                for target in (None, "C:\\Temp\\pv.wav"):
                    for snd in (True, False):
                        reset_globals()
                        m.ZOOM["play"], m.ZOOM["sound"] = list(zp), snd
                        args = [P, 5.0, 10.0, jsonable(pieces), True, naudio, target]
                        out["preview_audio_cmd"].append({"globals": globals_state(), "args": args,
                                                         **call(m.preview_audio_cmd, P, 5.0, 10.0, pieces, True,
                                                                naudio, target)})
    for kind in ("fast", "slow", "zoom"):
        for path in (None, P):
            for rate in (44100, 48000):
                reset_globals()
                out["effect_sample_cmd"].append({"globals": globals_state(), "args": [kind, path, 12.5, "C:\\Temp\\fx.wav", rate],
                                                 **call(m.effect_sample_cmd, kind, path, 12.5, "C:\\Temp\\fx.wav", rate)})
    for t, w, h, crop, trc in ((0.0, 320, 180, None, None), (12.3456, 640, 360, (1440, 1080), None),
                               (99.9995, 1600, 900, None, "arib-std-b67")):
        with Capture() as cap:
            m.grab_frame(P, t, w, h, crop, trc)
        out["grab_frame"].append({"args": [P, t, w, h, jsonable(crop), trc], "commands": cap.cmds})
    for times, trc in (([0.5 * k for k in range(15)], None), ([1.0, 2.0], "smpte2084"), ([], None)):
        with Capture() as cap:
            n = m.grab_frames(P, times, 128, 72, trc)
        out["grab_frames"].append({"args": [P, times, 128, 72, trc], "returned": n,
                                   "commands": cap.cmds,
                                   "note": "batch commands come from Popen (made to fail here), then one grab_frame "
                                           "fallback command per time of that batch"})
    sources = {"two clips": [("C:\\clips\\a.mp4", 8.8), ("C:\\clips\\b.mp4", 0.0)],
               "one clip": [("C:\\clips\\a.mp4", 0.0), ("C:\\clips\\a.mp4", 8.8)], "no clips": None}
    for name in m.TRANSITIONS:
        for sname, src in sources.items():
            fake = types.SimpleNamespace(_trans_sources=lambda src=src: src)
            with Capture() as cap:
                m.App._trans_frames(fake, name)
            out["transition_preview"].append({"transition": name, "sources": sname, "source_list": jsonable(src),
                                              "commands": cap.cmds})
    reset_globals()
    return out


# ------------------------------------------------------------------ exports.json (Encoder with a fake file system)
class FakeExport:
    """Runs Encoder.encode without ffmpeg. Every ffmpeg run "writes" a file whose size is
    (video + audio bitrate) * duration / 8 * overshoot, so the pass search behaves like a real encoder."""

    def __init__(self, overshoot=(1.0,), fail_hw=False, fail_all=False):
        self.overshoot, self.fail_hw, self.fail_all = list(overshoot), fail_hw, fail_all
        self.files, self.runs, self.status, self.progress = {}, [], [], []

    def size_for(self, cmd, duration):
        kb = int(cmd[cmd.index("-b:v") + 1].rstrip("k"))
        a_bps = 0
        if "-b:a" in cmd:
            n_maps = sum(1 for i, x in enumerate(cmd) if x == "-map") - 1
            a_bps = int(cmd[cmd.index("-b:a") + 1].rstrip("k")) * 1000 * n_maps
        k = len([r for r in self.runs if r["ok"]])
        f = self.overshoot[min(k, len(self.overshoot) - 1)]
        return int((kb * 1000 + a_bps) * duration / 8 * f)

    def __enter__(self):
        fx = self
        self._run = m.Encoder._run
        self._getsize, self._replace, self._remove = os.path.getsize, os.replace, os.remove

        def fake_run(enc, cmd, duration, label, pbase, pspan):
            ok = not fx.fail_all and not (fx.fail_hw and "-hwaccel" in cmd)
            entry = {"cmd": list(cmd), "duration": duration, "label": label, "pbase": pbase, "pspan": pspan, "ok": ok}
            if ok:
                entry["size"] = fx.size_for(cmd, duration)
                fx.files[cmd[-1]] = entry["size"]
            fx.runs.append(entry)
            return ok, "" if ok else "simulated ffmpeg error"

        def getsize(p):
            return fx.files[p] if p in fx.files else self._getsize(p)

        def replace(a, b):
            if a in fx.files:
                fx.files[b] = fx.files.pop(a)
            else:
                self._replace(a, b)

        def remove(p):
            if p in fx.files:
                del fx.files[p]
            elif p.startswith("C:\\out\\"):
                raise FileNotFoundError(p)
            else:
                self._remove(p)

        m.Encoder._run = fake_run
        os.path.getsize, os.replace, os.remove = getsize, replace, remove
        return self

    def __exit__(self, *exc):
        m.Encoder._run = self._run
        os.path.getsize, os.replace, os.remove = self._getsize, self._replace, self._remove
        return False


def export_st(**kw):
    st = {"target_mb": 50.0, "bitrate_mbps": None, "res_mode": "Auto", "codec": "hevc_nvenc", "audio_k": "128k",
          "ratio": None, "xfade": None, "T": 0.4, "tonemap": False, "fast_audio": "VHS effect with sound",
          "slow_audio": "Slow-mo effect with sound", "slow_pitch": "Slows down with the video (deep)",
          "fast_pitch": "Stays the same (compressed)", "vhs": False, "slow_interp": "Frame blending",
          "fast_vol": 70, "slow_vol": 150, "zoom_fx": "Zoom motion", "zoom_snd": "Whoosh sound", "zoom_len": "0.4",
          "zoom_vol": 80, "mix_audio": False}
    st.update(kw)
    return st


def export_cases():
    A, B, C = "C:\\clips\\a.mp4", "C:\\clips\\b.mkv", "C:\\clips\\c.mov"
    mixed = [{"a": 2.0, "b": 5.0, "speed": 4.0}, {"a": 9.0, "b": 11.0, "speed": 0.5}]
    big = dict(bitrate=60_000_000.0)
    return [
        ("size target, first pass fits", [dict(path=A, end=60.0, **big)], export_st(), dict(overshoot=(0.95,))),
        ("size target, overshoots then fits", [dict(path=A, end=60.0, **big)], export_st(),
         dict(overshoot=(1.10, 1.04, 0.97))),
        ("size target, constant overshoot is corrected", [dict(path=A, end=60.0, **big)], export_st(),
         dict(overshoot=(1.5,))),
        # each pass overshoots 20% more than the last: beats the 0.985 and 0.92 corrections -> 6 passes, error
        ("size target, never fits -> error", [dict(path=A, end=60.0, **big)], export_st(),
         dict(overshoot=(1.5, 1.8, 2.16, 2.592, 3.1104, 3.73248))),
        ("size target, undershoots a lot", [dict(path=A, end=60.0, **big)], export_st(),
         dict(overshoot=(0.6, 0.8, 0.95))),
        ("capped by the source bitrate", [dict(path=A, end=10.0, bitrate=4_000_000.0)], export_st(target_mb=200.0),
         dict(overshoot=(0.5,))),
        ("too small for the length", [dict(path=A, end=600.0)], export_st(target_mb=1.0), {}),
        ("fixed bitrate", [dict(path=A, end=30.0)], export_st(target_mb=None, bitrate_mbps=12.5, res_mode="Original"), {}),
        ("fixed bitrate, 50% resolution, h264", [dict(path=A, end=30.0, w=2560, h=1440)],
         export_st(target_mb=None, bitrate_mbps=4.0, res_mode="50%", codec="h264_nvenc"), {}),
        ("fixed bitrate, CUDA decode fails once", [dict(path=A, end=30.0)],
         export_st(target_mb=None, bitrate_mbps=8.0), dict(fail_hw=True)),
        ("size target, CUDA decode fails once", [dict(path=A, end=30.0, **big)], export_st(),
         dict(fail_hw=True, overshoot=(0.95,))),
        ("ffmpeg always fails", [dict(path=A, end=30.0)], export_st(target_mb=None, bitrate_mbps=8.0),
         dict(fail_all=True)),
        ("join 3 with crossfade", [dict(path=A, end=20.0, **big), dict(path=B, end=15.0, **big),
                                   dict(path=C, start=3.0, end=12.0, **big)],
         export_st(xfade="fade", T=0.5), dict(overshoot=(1.02, 0.96))),
        ("join 2 glitch, crop 21:9, mixed audio", [dict(path=A, w=3440, h=1440, naudio=2, **big),
                                                   dict(path=B, naudio=1, **big)],
         export_st(xfade="glitch", ratio=21 / 9, mix_audio=True), dict(overshoot=(0.93,))),
        # 1.0.8 quirk: with a hard cut T is still subtracted from the total length (bitrate + progress use it)
        ("join 2 hard cut (T still subtracted)", [dict(path=A, end=20.0, **big), dict(path=B, end=15.0, **big)],
         export_st(xfade=None, T=0.4), dict(overshoot=(0.95,))),
        ("join, transition longer than allowed", [dict(path=A, end=1.0, **big), dict(path=B, end=10.0, **big)],
         export_st(xfade="fade", T=2.0), dict(overshoot=(0.95,))),
        ("join, transition too short -> hard cut", [dict(path=A, end=0.1, **big), dict(path=B, end=10.0, **big)],
         export_st(xfade="fade", T=0.4), dict(overshoot=(0.95,))),
        ("speed sections + vhs + RIFE not installed", [dict(path=A, sections=mixed, **big)],
         export_st(vhs=True, slow_interp="RIFE AI (smoothest, needs a GPU)"), dict(overshoot=(0.95,))),
        ("HDR tonemap on", [dict(path=A, w=3840, h=2160, end=20.0, **HDR, **big)], export_st(tonemap=True),
         dict(overshoot=(0.95,))),
        ("HDR tonemap off", [dict(path=A, w=3840, h=2160, end=20.0, **HDR, **big)], export_st(),
         dict(overshoot=(0.95,))),
        ("two audio tracks kept (metadata names)", [dict(path=A, naudio=2, end=20.0, **big)], export_st(),
         dict(overshoot=(0.95,))),
        ("no audio", [dict(path=A, naudio=0, end=20.0, **big)], export_st(audio_k="64k"), dict(overshoot=(0.95,))),
        ("per-clip preset override", [dict(path=A, end=20.0)],
         dict(export_st(), **{"bitrate_mbps": 6.0, "target_mb": None, "res_mode": "Original"}), {}),
    ]


def dump_exports():
    out = []
    for name, rows, st, fx_kw in export_cases():
        reset_globals()
        cases = [clip_case(**r) for r in rows]
        clips = [copy.deepcopy(c["clip"]) for c in cases]
        for c in clips:
            if c["pieces"]:
                c["pieces"] = [tuple(p) for p in c["pieces"]]
            c["zooms"] = [tuple(z) for z in c["zooms"]]
        target = "C:\\out\\result.mp4"
        with FakeExport(**fx_kw) as fx:
            enc = m.Encoder(progress_cb=fx.progress.append, status_cb=fx.status.append)
            res = call(enc.encode, target, clips, dict(st), 0.0, 100.0, "")
        entry = {"name": name, "st": jsonable(st), "fake": jsonable(fx_kw), "clips": cases, "out": target}
        entry.update(res)
        entry.update({"runs": fx.runs, "status": fx.status, "progress": fx.progress,
                      "files_left": jsonable(fx.files), "globals_after": globals_state()})
        out.append(entry)
    reset_globals()
    return out


# ------------------------------------------------------------------ files (settings, presets, projects)
def dump_files():
    """Sample settings / presets / project files the C# app must read. Returns what Python parses from them."""
    os.makedirs(FILES, exist_ok=True)
    presets = {"presets": [
        {"name": "Discord", "mbps": 4, "key": "q"},
        {"name": "High", "mbps": 25.5, "key": "W"},
        {"name": "Reserved key", "mbps": 8, "key": "space"},
        {"name": "Discord", "mbps": 9, "key": "e"},
        {"name": "", "mbps": 5, "key": "r"},
        {"name": "Zero", "mbps": 0, "key": "t"},
        {"name": "  Padded  ", "mbps": "12", "key": ""},
        {"name": "No key", "mbps": 3},
    ]}
    paths = {"presets": os.path.join(FILES, "presets.json"),
             "presets_broken": os.path.join(FILES, "presets_broken.json"),
             "presets_badtype": os.path.join(FILES, "presets_badtype.json")}
    with open(paths["presets"], "w", encoding="utf-8") as f:
        json.dump(presets, f, indent=1)
    with open(paths["presets_broken"], "w", encoding="utf-8") as f:
        f.write('{"presets": [ {"name": "x", "mbps": ')
    with open(paths["presets_badtype"], "w", encoding="utf-8") as f:
        json.dump({"presets": [{"name": "ok", "mbps": 5, "key": "a"}, {"name": "bad", "mbps": "fast"}]}, f, indent=1)
    parsed = {}
    old = m.PRESETS_FILE
    try:
        for k, p in paths.items():
            m.PRESETS_FILE = p
            parsed[os.path.basename(p)] = m.load_presets()
        m.PRESETS_FILE = os.path.join(FILES, "does_not_exist.json")
        parsed["missing file"] = m.load_presets()
    finally:
        m.PRESETS_FILE = old
    projects = {
        "two_clips.clipproj": {
            "app": "Clip Shrinker", "format": 1, "active": 1,
            "clips": [
                {"path": "C:\\clips\\a.mp4", "start": 1.5, "end": 18.25, "preset": "Discord",
                 "sections": [{"a": 2.0, "b": 5.0, "speed": 4.0}, {"a": 9.0, "b": 11.0, "speed": 0.5}],
                 "zooms": [{"a": 3.0, "b": 7.0, "x": 0.25, "y": 0.2, "w": 0.4}], "rel": "..\\clips\\a.mp4"},
                {"path": "C:\\clips\\b.mkv", "start": 0.0, "end": 9.0, "preset": None, "sections": [], "zooms": []}],
            "settings": {"size": "50", "bitrate": "", "res": "Auto", "codec": "HEVC (hevc_nvenc)", "crop": "16:9",
                         "audio": "128k", "atrk": "Keep separate tracks", "mute_mic": False,
                         "fast_audio": "VHS effect with sound", "slow_audio": "Slow-mo effect with sound",
                         "slow_pitch": "Slows down with the video (deep)",
                         "fast_pitch": "Stays the same (compressed)", "vhs": True, "fast_vol": 70, "slow_vol": 150,
                         "zoom_fx": "Zoom motion", "zoom_snd": "Whoosh sound", "zoom_len": "0.4", "zoom_vol": 80,
                         "slow_interp": "Frame blending", "hdr": True, "mode": "join",
                         "trans": "Crossfade (gradient)", "tlen": "0.4"}},
        "messy_values.clipproj": {
            "app": "Clip Shrinker", "format": 1, "active": 7,
            "clips": [
                {"path": "C:\\clips\\a.mp4", "start": "2", "end": 99999, "preset": 5,
                 "sections": [{"a": 1, "b": 2, "speed": 500}, {"a": 5, "b": 4, "speed": 2}, {"a": "x"}],
                 "zooms": [{"a": 1, "b": 3, "x": -1, "y": 2, "w": 0.01}]},
                "not a clip"],
            "settings": {"size": 50, "res": "Huge", "codec": "VP9", "zoom_len": 0.6, "zoom_vol": 999,
                         "fast_vol": True, "vhs": "yes", "hdr": False, "trans": "Unknown"}},
        "minimal.clipproj": {"clips": []},
    }
    for name, data in projects.items():
        with open(os.path.join(FILES, name), "w", encoding="utf-8") as f:
            json.dump(data, f, indent=1)
    return {"load_presets": parsed,
            "projects": list(projects),
            "note": "Project loading lives in the Tk App (load_project / _load_clips / _apply_project_settings), so "
                    "it is not run here. The .clipproj files are inputs for the C# loader; see README for the rules."}


def copy_user_settings():
    """The real settings.json written by 1.0.8 on this PC (window geometry blanked)."""
    src = os.path.join(ROOT, "..", "Dev", "library", "settings.json")
    try:
        with open(src, encoding="utf-8") as f:
            d = json.load(f)
    except (OSError, ValueError):
        return None
    if "geometry" in d:
        d["geometry"] = "1200x800+100+100"
    with open(os.path.join(FILES, "settings.json"), "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1)
    old = m.SETTINGS_FILE
    try:
        m.SETTINGS_FILE = os.path.join(FILES, "settings.json")
        return m.load_settings()
    finally:
        m.SETTINGS_FILE = old


# ------------------------------------------------------------------ main
def main():
    os.makedirs(OUT, exist_ok=True)
    meta = {"reference": "tools/reference/video_shrinker_1.0.8.py",
            "reference_sha256": hashlib.sha256(open(REF, "rb").read()).hexdigest(),
            "app_version": m.APP_VERSION, "python": sys.version.split()[0],
            "globals_defaults": jsonable(DEFAULTS),
            "constants": jsonable({k: getattr(m, k) for k in (
                "ASPECTS", "TRANSITIONS", "CODECS", "MAX_AUDIO", "MIX", "SPEED_MIN", "SPEED_MAX", "MIN_SECTION",
                "VHS_MIN", "ZOOM_MIN_W", "FAST_AUDIO", "SLOW_AUDIO", "SLOW_PITCH", "FAST_PITCH", "SLOW_INTERP",
                "ZOOM_FX", "ZOOM_AUDIO", "RES_SIZE_MODE", "RES_BITRATE_MODE", "TICK_STEPS", "MIN_VIEW",
                "KEY_DEFAULTS", "RESERVED_KEYS", "RESERVED_PLAIN", "RESERVED_CTRL", "KEY_SHOWN", "THUMB_BATCH",
                "PV_W", "PV_H", "PV_FPS", "MAX_PV", "PLAY_FPS_CAP", "APP_ID", "PROJECT_EXT")})}
    meta["constants"]["RESERVED_KEYS"] = sorted(m.RESERVED_KEYS)
    meta["constants"]["RESERVED_PLAIN"] = sorted(m.RESERVED_PLAIN)
    meta["constants"]["RESERVED_CTRL"] = sorted(m.RESERVED_CTRL)
    outputs = {
        "meta.json": meta,
        "helpers.json": dump_helpers(),
        "pieces.json": dump_pieces(),
        "filters.json": dump_filters(),
        "graphs.json": dump_graphs(),
        "preview.json": dump_preview(),
        "exports.json": dump_exports(),
    }
    files = dump_files()
    files["settings_parsed"] = copy_user_settings()
    outputs["files.json"] = files
    for name, data in outputs.items():
        with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=1, ensure_ascii=False, allow_nan=False)
            f.write("\n")
    counts = {
        "graphs": len(outputs["graphs.json"]), "exports": len(outputs["exports.json"]),
        "filters": sum(len(v) for v in outputs["filters.json"].values()),
        "preview": sum(len(v) for v in outputs["preview.json"].values()),
        "helpers": sum(len(v) for v in outputs["helpers.json"].values()),
        "pieces": sum(len(v) for v in outputs["pieces.json"].values()),
    }
    errors = [g["name"] for g in outputs["graphs.json"] if "error" in g]
    print("fixtures written to", OUT)
    print(json.dumps(counts))
    if errors:
        print("graph cases that raised:", errors)


if __name__ == "__main__":
    main()
