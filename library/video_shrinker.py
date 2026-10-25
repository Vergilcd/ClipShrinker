"""
Clip Shrinker - trim, crop, join and shrink video clips with NVIDIA NVENC.

Requirements:
  * Windows, NVIDIA GPU with NVENC (GTX 10xx or newer)
  * Python 3.9+ (tkinter is included in the normal python.org installer)
  * ffmpeg.exe and ffprobe.exe, either next to this file or on PATH
No pip packages needed.
"""
import array
import base64
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import tkinter as tk
from fractions import Fraction
from tkinter import filedialog, messagebox, ttk

try:
    import winsound          # Windows only: used for preview audio
except Exception:
    winsound = None

try:
    import sv_ttk            # Windows 11 style theme (bundled in the library folder)
except Exception:
    sv_ttk = None

APP_DIR = os.path.dirname(os.path.abspath(sys.argv[0] if getattr(sys, "frozen", False) else __file__))
NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)

SETTINGS_FILE = os.path.join(APP_DIR, "settings.json")
SESSION_FILE = os.path.join(APP_DIR, "last_session.json")
PROJECT_EXT = ".clipproj"
PALETTE = {
    "dark": {"bg": "#1c1c1c", "card": "#2b2b2b", "border": "#3f3f3f", "fg": "#ffffff",
             "muted": "#9d9d9d", "accent": "#4cc2ff", "accent_text": "#60cdff",
             "foot": "#141414", "footline": "#3a3a3a"},
    "light": {"bg": "#fafafa", "card": "#ffffff", "border": "#d0d0d0", "fg": "#1b1b1b",
              "muted": "#6b6b6b", "accent": "#0067c0", "accent_text": "#0067c0",
              "foot": "#ececec", "footline": "#cdcdcd"},
}


# Red version of the theme's blue "accent" button (6 small PNG states per theme, made from the sv_ttk sprites).
DANGER_SPRITES = {"dark": {"focus-hover": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAA1UlEQVR42u2VMW7CQBAA59aynMNIWCLpuY4KUUfKD6iQ0kbiRfwhrZUvpMwDKBDd0SNkIVBwgrhN4ZYizqX0PGB2t5k1M5vpom+Zj0cwHECa0IrLFfYHys2W19MZ8/ZQ6PxxAl8CteKDtvI5MXBnIAuUHyuMPk2VvMB/BmJwucCxQhgOoFaiOSvcFwhp0vrMW/igkCYI/0wn7ISd8HfCy7VJUCROmjYK+0PTs1iswK5Cys0WsoDrSTPlD5u5XCANLNceM7OZvvQtzzEvYFexXHve629+AE6PQ2ALB222AAAAAElFTkSuQmCC", "focus": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAA00lEQVR42u2VMYrCUBRFz/tJJgZEP2TsLe20FtyBlTDtgCtyD9MGtzClC7AQO+1VjCCo+YY3RdopzPwpcxZw7nvNvTJNYp23E2aDPqRdiAJq4Uo4Xci2e76uN2TZszobD+EZQiHkWs9nBXhTCB3Zao3oZKR0UvKH4INtKeRHDGkXCj8ZAA+Bd4shCmq/+Ru5AlGA4Z9phI2wEb4mdGVVQZ5YUXAlhtOl6jNfYuBwxmTbPYRPbKxVyh8usy2FwLHY7JBpEutnO+HDZwIOZxabHd/3gh8EXkQCeqT9dgAAAABJRU5ErkJggg==", "hover": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAA5UlEQVR42u3VPUrEUBiF4ffeDzNJkUm0EBTUQrAwoI2LsJhdWE7nOlyEjeAK7C1tLEQUS8lMKfkRGbnJzecGLIyZMu8CHjjVMbNoogdRxMXePlk8JRChT857nuqK60XO+2qFmW+lenWc4axQAp5+CZACYddx+fKMeTw90cMk5YNhbRp4KwpsFk8pGV6tcJYk2ECk98zf8oBYwbLmRnAER/BP4JdvkTVAAny3LfahqghVB4MbqtyXBfZ2kVM3DdIp/MdVkE75dI6bPMfMooluhyHnO7scxTGB7XkBnee1rrlbLikaxw/pAk/vJwzO7wAAAABJRU5ErkJggg==", "pressed": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAA2klEQVR42u3VMUoDQRSA4f/NsrgyzMaQLqZJFZIIXsQDeARvIblMyDE8QdoQsNxNpURnCQyGmWcj2Lpuyv0P8PFe8548XF/ppCh4HN8ydw6bGdp0iold07A51FQhIE/Dga5mM4JkeFXOtCsHrAhWI8/7PfKyXOi0HPCuSpdGIrz6D8zcOY4dMYCjKveuxNjMkOheAorMYLhwPdiDPfgn8BQT+YUmCzFidk2DFekMDkXYeo/ZHGqsRm5E/rW/+bmFeYqsqxr5fQFj7lxJ0fIFhJjY+k/WVc3b+YtvqadLfoOY1tEAAAAASUVORK5CYII=", "rest": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAA4UlEQVR42u3UsUrDUBiG4fecE9okYJPSSVCoixAoingbHbwNQW9GcOwteBUubgpBx5ri2BiTJTQm53dxdPCQjnkv4IFv+dQyGMtJGHI9n5NEERgPp7qWt7Lkfr0mq2vU7Wwqd2fnYDwqwLpxaGDyC9+8PKNeLy8kmc74ol+xgjTfopMooqJ/lcAijtEYz3nmX1kA46HZcwM4gAP4P7Bt96JqgPYb/VQUjGz/exiJ5TH/RK82GUXToDsLIu6SCFhLvtuxyt5Ry2Ash77P1dExpwcTAmOcvLrrSMuSh48NRdPwA9t0Tc4UKSvmAAAAAElFTkSuQmCC"}, "light": {"focus-hover": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAA20lEQVR42u2Vv23CQBxG352MLnYRGho7/ElBjYSFMgMVGyAkpmCFDJABMgIlS0QKLoOEFBPilgjJh0D6UdwCmEvpN8DTV71PyXIq7A7w/gFZgSovVEHCAAYxzFJ4ekTJ20RYrKDTg3YMD6aSEHuC3R7yb3gdo+SlLRwj6D/jxdcWmhZNVkAnxptuAp8FWpUXMMZfaAyqPKP5Z2phLayFNwklDFyCfLEWiRpoBrHrmS/5LwwTNLPUxXGzvW+pPbkW7nOYj1CynAo/f+4C1gWqPFe8gAakCcxH0Iq4AgfVR4XORgxxAAAAAElFTkSuQmCC", "focus": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAA7ElEQVR42u3VMUrDYBjG8f8bUmI6iENBUiv0BAVDELyBkzcoLT1BRs/g1MX98wgdvYGDSNELfFDLFx1EXZKPFt4Org7GdMz/AD+e7RFdjJXXL7h7gpcCKbfUSeMQRglMUjg5RPT2Srm+Z358yjRJIIpqgXiPcY68WMHNJaLnA51/HDAdDmmSsZa85xGNQ/3MLuov+2Xp0eMDgZTb5hhAFCHlhoA914It2IJ/AjUOwfvG0LKq0G6HgFGCca45WDg46xMwScnfVhhrWVbVv5YZa8nf1zDLEF2MlfX3zwU8F0i5qXkBHUj7MMug12UHDKlUzuGj3UkAAAAASUVORK5CYII=", "hover": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAAv0lEQVR42u3OsUrDUBiG4SdpqksQI+ImgnfgJDgL3oKLF+nq4i3kEhxtBqVQ2ybpOcchDo6mZuw7/vA9/Fl6fkqWgXpF09Mno5pnXMy5KTmZKSwDL5+sIyEx0rM1bJueh0qhXg2H3Vjpp2TYriP1l1zTE/fEfhcTi06uT0QTgOiT3MQdwAN4AP8IxomohCh3lhF3//dC4DyXuz2l29C1pD2+TZF2S9hwVylURzxe8vrO2wdtGAcez7guub+iLHwDlOZNEHlOs3kAAAAASUVORK5CYII=", "pressed": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAAqElEQVR42u3UwW3CQBBA0be7Xim2EDeoAnoghVBkOkgRUAWcgoQT2WZzQEg5RXHw0f8+T5o5TChv++IycLxy6umLUVWBVWZTs0gql4H3Dz5vDCMx+HKfPXfslqLj9f/Yo6HQ3ji0olP/HPYTPXfi6Jv9VldEEzeDMziDfwOrMJ2Wg2iVSROgKbDOok3NS3wOTYEmsm1UFonXJYf2/nW7MnpN68y2oY6+AWK4NmOH4+RaAAAAAElFTkSuQmCC", "rest": "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAAwElEQVR42u3UMU4CQQBA0cdkwQqdQGKMjTHxDhYm1pZegENSewPuoOVSkUyykSy7y4y1nYuU/AO88k/KelWkgU1D3dEXo5pOuJ/xPOemUkkD6x37zJE00ouHwmfLtuN9qbJp2GdpcFKpYCB+ZzaNoO44EfvVEXUn6It0Bi9BVwRn7gJewAv4N3CSReXfUIRQBLeBfIbdHHvuguBlQW7FviXn8VDOYtei5XWhspyxeuCjFr92HEaiV4Gna94emU/9AOU3SyLyCrPoAAAAAElFTkSuQmCC"}}

APP_ID = "ClipShrinker.Desktop.App"


def set_window_taskbar_props(hwnd, exe_path, name):
    """Tells Windows how to relaunch this window (pin to taskbar -> ClipShrinker.exe, own name and icon)
    and puts it in the same group as that shortcut. Windows only; every error is ignored."""
    import ctypes
    import uuid
    from ctypes import POINTER, Structure, byref, c_ubyte, c_uint32, c_ushort, c_void_p, wintypes

    class GUID(Structure):
        _fields_ = [("a", c_uint32), ("b", c_ushort), ("c", c_ushort), ("d", c_ubyte * 8)]

    class PROPERTYKEY(Structure):
        _fields_ = [("fmtid", GUID), ("pid", c_uint32)]

    class PROPVARIANT(Structure):                    # 24 bytes on 64-bit Windows
        _fields_ = [("vt", c_ushort), ("r1", c_ushort), ("r2", c_ushort), ("r3", c_ushort),
                    ("p", c_void_p), ("p2", c_void_p)]

    def guid(text):
        u = uuid.UUID(text)
        return GUID(u.time_low, u.time_mid, u.time_hi_version, (c_ubyte * 8)(*u.bytes[8:]))

    shell32 = ctypes.windll.shell32
    shell32.SHGetPropertyStoreForWindow.argtypes = [wintypes.HWND, POINTER(GUID), POINTER(c_void_p)]
    store = c_void_p()
    iid = guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")           # IPropertyStore
    if shell32.SHGetPropertyStoreForWindow(hwnd, byref(iid), byref(store)) != 0 or not store.value:
        return
    vtbl = ctypes.cast(store, POINTER(POINTER(c_void_p)))[0]
    set_value = ctypes.WINFUNCTYPE(ctypes.c_long, c_void_p, POINTER(PROPERTYKEY), POINTER(PROPVARIANT))(vtbl[6])
    commit = ctypes.WINFUNCTYPE(ctypes.c_long, c_void_p)(vtbl[7])
    release = ctypes.WINFUNCTYPE(ctypes.c_ulong, c_void_p)(vtbl[2])
    fmt = guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3")           # System.AppUserModel.*
    try:
        for pid, text in ((5, APP_ID),                              # ID
                          (2, f'"{exe_path}"'),                     # RelaunchCommand
                          (3, f"{exe_path},0"),                     # RelaunchIconResource (icon inside the exe)
                          (4, name)):                               # RelaunchDisplayNameResource
            buf = ctypes.create_unicode_buffer(text)
            pv = PROPVARIANT(31, 0, 0, 0, ctypes.addressof(buf), 0)  # VT_LPWSTR
            set_value(store, byref(PROPERTYKEY(fmt, pid)), byref(pv))
        commit(store)
    finally:
        release(store)


def load_settings():
    try:
        with open(SETTINGS_FILE, encoding="utf-8") as f:
            d = json.load(f)
        return d if isinstance(d, dict) else {}
    except Exception:
        return {}


PRESETS_FILE = os.path.join(APP_DIR, "presets.json")
RESERVED_KEYS = {"s", "d", "m", "1", "2", "kp_1", "kp_2", "space", "return", "kp_enter", "escape", "tab",
                 "delete", "backspace", "f1", "f2"}


def load_presets():
    """Bitrate presets: [{"name": str, "mbps": float, "key": str (keysym, lower case, may be empty)}]."""
    out = []
    try:
        with open(PRESETS_FILE, encoding="utf-8") as f:
            data = json.load(f)
        for d in (data.get("presets") if isinstance(data, dict) else []) or []:
            name, mbps, key = str(d.get("name", "")).strip(), float(d.get("mbps", 0)), str(d.get("key", "")).lower()
            if name and mbps > 0 and all(name != o["name"] for o in out):
                out.append({"name": name, "mbps": mbps, "key": "" if key in RESERVED_KEYS else key})
    except Exception:
        pass
    return out


def save_presets(lst):
    try:
        with open(PRESETS_FILE, "w", encoding="utf-8") as f:
            json.dump({"presets": lst}, f, indent=1)
    except Exception:
        pass


def save_settings(d):
    try:
        with open(SETTINGS_FILE, "w", encoding="utf-8") as f:
            json.dump(d, f, indent=1)
    except Exception:
        pass


def system_theme():
    """'dark' or 'light' following the Windows app setting (dark when unknown)."""
    try:
        import winreg
        k = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                           r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
        return "light" if winreg.QueryValueEx(k, "AppsUseLightTheme")[0] else "dark"
    except Exception:
        return "dark"


# Crash/diagnostic log (useful because the app runs without a console window).
try:
    import faulthandler
    import traceback
    _LOG = open(os.path.join(APP_DIR, "clipshrinker.log"), "w", buffering=1)
    faulthandler.enable(_LOG)
except Exception:
    _LOG = None

PREVIEW_W, PREVIEW_H = 640, 360     # initial preview size (the splitter makes it larger/smaller)
MAX_PV = (1600, 900)                # largest frame the preview will render
PLAY_FPS_CAP = 30
THUMB_H = 72
TL_H = 133           # timeline canvas height
TIMES_H = 0          # (the Start / End / Length line is gone: the handles have their own time boxes)
COMPACT_TL_H, COMPACT_LANE_H = 42, 4
TL_BG = "#202020"    # timeline background     # thin strip used for the clips that are not active
LANE_H = 30          # top lane of the timeline (speed-section bars + multiplier boxes)
ZLANE_H = 18          # thin lane below the speed lane: zoom sections (white)
PAD = 14             # left/right padding inside timeline
HANDLE_GRAB = 10
VIDEO_EXTS = (".mp4", ".mov", ".mkv", ".m4v", ".avi", ".webm")

ASPECTS = {                       # name -> width/height ratio (None = keep as is)
    "Original (no crop)": None,
    "21:9": 21 / 9,
    "16:9": 16 / 9,
    "16:10": 16 / 10,
    "4:3": 4 / 3,
}

# name -> ffmpeg xfade transition (None = hard cut)
TRANSITIONS = {
    "Hard cut": None,
    "Crossfade (gradient)": "fade",
    "Fade through black": "fadeblack",
    "Fade through white": "fadewhite",
    "Glitch (digital)": "glitch",
    "Gradient wipe, left to right": "smoothright",
    "Gradient wipe, right to left": "smoothleft",
    "Gradient wipe, upwards": "smoothup",
    "Gradient wipe, downwards": "smoothdown",
    "Swipe left (push)": "slideleft",
    "Swipe right (push)": "slideright",
    "Swipe up (push)": "slideup",
    "Swipe down (push)": "slidedown",
    "Circle open": "circleopen",
    "Circle close": "circleclose",
    "Circle crop": "circlecrop",
    "Clock wipe (radial)": "radial",
    "Diagonal wipe": "diagtl",
    "Zoom in": "zoomin",
    "Pixelize": "pixelize",
    "Blur (horizontal)": "hblur",
    "Distance": "distance",
}

PV_W, PV_H, PV_FPS = 240, 136, 15
GLITCH = "glitch"                           # not an xfade mode: fast cross + glitch filters (see glitch_parts)


def xfade_mode(x):
    return "fadefast" if x == GLITCH else x


def glitch_parts(src, out, a, T, W, H, tag, seed=7):
    """Digital "glitch" over [a, a+T] of [src]: RGB split, displaced picture bands, noise and a flash -> [out].
    Plain filters with time windows (fast; a per-pixel xfade expression would be ~50x slower)."""
    import random
    rnd = random.Random(seed)
    b, mid = a + T, a + T / 2
    en = lambda x, y: f"enable='between(t,{x:.3f},{y:.3f})'"
    s1 = max(2, W // 110)                                       # RGB split strength in pixels
    chain = [f"rgbashift=rh={-s1}:bh={s1}:edge=smear:{en(a, b)}",
             f"rgbashift=rh={2 * s1}:gv={-s1}:bh={-2 * s1}:edge=smear:{en(mid - T * 0.18, mid + T * 0.18)}",
             f"noise=alls=28:allf=t:{en(a + T * 0.15, b - T * 0.15)}",
             f"lutyuv=y='val*1.35':{en(mid - T * 0.06, mid + T * 0.04)}"]
    nb = 6
    parts = [f"[{src}]" + ",".join(chain) + f",split=2[{tag}m0][{tag}s]",
             f"[{tag}s]split={nb}" + "".join(f"[{tag}s{k}]" for k in range(nb))]
    cur = f"{tag}m0"
    for k in range(nb):                                         # bands of the picture jump sideways
        h = max(2, int(H * rnd.uniform(0.03, 0.14)) // 2 * 2)
        y = int(rnd.uniform(0, H - h)) // 2 * 2
        dx = int(W * rnd.uniform(0.04, 0.22)) * rnd.choice((-1, 1))
        t1 = a + T * rnd.uniform(0.05, 0.75)
        t2 = min(b, t1 + T * rnd.uniform(0.12, 0.3))
        nxt = out if k == nb - 1 else f"{tag}m{k + 1}"
        parts.append(f"[{tag}s{k}]crop={W}:{h}:0:{y}[{tag}c{k}]")
        parts.append(f"[{cur}][{tag}c{k}]overlay=x={dx}:y={y}:{en(t1, t2)}[{nxt}]")
        cur = nxt
    return parts           # transition hover preview size / frame rate

_TEMP_WAVS = []


# ------------------------------------------------------------------ helpers
def find_tool(name):
    local = os.path.join(APP_DIR, name + ".exe")
    if os.path.isfile(local):
        return local
    return shutil.which(name)


FFMPEG = find_tool("ffmpeg")
FFPROBE = find_tool("ffprobe")


def _log(msg):
    if _LOG:
        try:
            _LOG.write(str(msg) + "\n")
        except Exception:
            pass


def run_capture(cmd, binary=False):
    return subprocess.run(cmd, capture_output=True, creationflags=NO_WINDOW, text=not binary)


def fmt_elapsed(sec):
    """3 minutes 53 seconds / 1 hour 2 minutes 5 seconds / 12 seconds"""
    sec = int(round(max(0.0, sec)))
    h, m, s_ = sec // 3600, sec % 3600 // 60, sec % 60
    unit = lambda n, w: f"{n} {w}" + ("" if n == 1 else "s")
    out = ([unit(h, "hour")] if h else []) + ([unit(m, "minute")] if h or m else []) + [unit(s_, "second")]
    return " ".join(out)


def fmt_time(t):
    t = max(0.0, t)
    m, s = divmod(t, 60)
    h, m = divmod(int(m), 60)
    return f"{h}:{m:02d}:{s:05.2f}" if h else f"{m}:{s:05.2f}"


def parse_clock(txt):
    """'83.5', '1:23.5', '1:02:03' -> seconds (None when it is not a time)."""
    txt = txt.strip().replace(",", ".").replace(";", ":")
    if not txt:
        return None
    try:
        parts = [float(x) for x in txt.split(":")]
    except ValueError:
        return None
    if len(parts) > 3 or any(x < 0 for x in parts):
        return None
    t = 0.0
    for x in parts:
        t = t * 60 + x
    return t


def fmt_tick(t):
    t = int(round(t))
    h, rem = divmod(t, 3600)
    m, sec = divmod(rem, 60)
    return f"{h}:{m:02d}:{sec:02d}" if h else f"{m}:{sec:02d}"


RES_SIZE_MODE = ["Auto", "100%", "75%", "50%"]
RES_BITRATE_MODE = ["Original", "75%", "50%"]
TICK_STEPS = [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200]
MIN_VIEW = 1.0           # smallest zoomed-in timeline length in seconds
MAX_THUMB_CACHE = 800


def parse_fps(s):
    try:
        a, b = s.split("/")
        return float(a) / float(b)
    except Exception:
        try:
            return float(s)
        except Exception:
            return 30.0


def probe(path):
    r = run_capture([FFPROBE, "-v", "error", "-print_format", "json",
                     "-show_streams", "-show_format", path])
    if r.returncode != 0:
        raise RuntimeError((r.stderr or "").strip() or "ffprobe failed")
    j = json.loads(r.stdout)
    v = next((s for s in j["streams"] if s["codec_type"] == "video"), None)
    if not v:
        raise RuntimeError("No video stream found.")
    naudio = min(MAX_AUDIO, sum(1 for s in j["streams"] if s["codec_type"] == "audio"))
    trc = v.get("color_transfer")
    hdr_trc = trc if trc in ("smpte2084", "arib-std-b67") else None      # PQ (HDR10) or HLG
    dur = float(j["format"].get("duration") or v.get("duration") or 0)
    return {
        "w": int(v["width"]), "h": int(v["height"]),
        "fps": parse_fps(v.get("avg_frame_rate") or v.get("r_frame_rate", "30/1")),
        "dur": dur, "audio": naudio > 0, "naudio": naudio,
        "bitrate": float(j["format"].get("bit_rate") or 0),
        "size": int(j["format"].get("size") or os.path.getsize(path)),
        "trc": hdr_trc, "prim": v.get("color_primaries"), "space": v.get("color_space"),
    }


def even(n):
    """Round down to an even number (video dimensions must be even)."""
    return max(2, int(n) // 2 * 2)


def crop_size(w, h, ratio):
    """Centered crop to `ratio` (width/height). Only cuts the sides of clips that are
    wider than the target ratio; clips that are already narrower are left alone."""
    if not ratio or w / h <= ratio + 0.005:
        return w, h
    return min(w, even(h * ratio)), h


def choose_scale(mode, w, h, fps, v_bps):
    """mode: 'Auto', 'Original', '100%', '75%', '50%'. Returns scale factor (0.5 .. 1.0)."""
    if mode == "Original":
        return 1.0
    if mode != "Auto":
        return int(mode.rstrip("%")) / 100
    for s in (1.0, 0.75, 0.5):
        bpp = v_bps / (w * s * h * s * fps)
        if bpp >= 0.05:
            return s
    return 0.5


def tonemap_chain(trc):
    """HDR (PQ or HLG, BT.2020) -> SDR BT.709, 8-bit. Needs ffmpeg's zscale filter (zimg)."""
    return (f"zscale=tin={trc}:pin=2020:min=2020_ncl:t=linear:p=2020:npl=203,format=gbrpf32le,"
            "zscale=p=709,tonemap=tonemap=mobius:param=0.8:desat=0,"
            "zscale=t=709:m=709:r=tv,format=yuv420p")


def grab_frame(path, t, w, h, crop=None, trc=None):
    """One frame as base64 PNG (for Tk PhotoImage), optionally centered-cropped first.
    trc: HDR transfer name -> the frame is tone-mapped to SDR so it looks like the export."""
    vf = (f"crop={crop[0]}:{crop[1]}," if crop else "") + \
         f"scale={w}:{h}:force_original_aspect_ratio=decrease" + (("," + tonemap_chain(trc)) if trc else "")
    cmd = [FFMPEG, "-v", "error", "-ss", f"{t:.3f}", "-i", path, "-frames:v", "1",
           "-vf", vf, "-f", "image2pipe", "-vcodec", "png", "-"]
    r = run_capture(cmd, binary=True)
    return base64.b64encode(r.stdout) if r.returncode == 0 and r.stdout else None


class ThumbPool:
    """Worker threads that create the thumbnails, in small chunks.  The active clip is always served first (all
    workers help with it, and the other clips wait until it is done), then the others in list order.  A newer
    request of a clip replaces its older chunks."""

    def __init__(self, workers):
        self.workers = workers
        self.jobs = []
        self.active_running = 0
        self.cv = threading.Condition()
        for _ in range(workers):
            threading.Thread(target=self._run, daemon=True).start()

    def submit(self, row, fns):
        with self.cv:
            self.jobs = [j for j in self.jobs if j[0] is not row]
            self.jobs += [(row, fn) for fn in fns]
            self.cv.notify_all()

    def drop(self, row):
        with self.cv:
            self.jobs = [j for j in self.jobs if j[0] is not row]

    def _run(self):
        while True:
            with self.cv:
                while True:
                    if self.jobs:
                        job = min(self.jobs, key=lambda j: (0 if getattr(j[0], "active", False) else 1,
                                                            getattr(j[0], "index", 0)))
                        is_act = bool(getattr(job[0], "active", False))
                        if is_act or self.active_running == 0:
                            break
                    self.cv.wait()
                self.jobs.remove(job)
                if is_act:
                    self.active_running += 1
            try:
                job[1]()
            except Exception:
                if _LOG:
                    traceback.print_exc(file=_LOG)
            finally:
                with self.cv:
                    if is_act:
                        self.active_running -= 1
                    self.cv.notify_all()


THUMB_POOL = None
THUMB_THREADS = 8          # most ffmpeg processes making thumbnails at the same time (half the CPU threads, at most this)


def thumb_pool():
    global THUMB_POOL
    if THUMB_POOL is None:
        THUMB_POOL = ThumbPool(max(2, min(THUMB_THREADS, (os.cpu_count() or 4) // 2)))
    return THUMB_POOL


PNG_END = b"IEND\xaeB`\x82"
THUMB_BATCH = 12


def grab_frames(path, times, w, h, trc=None, stop=lambda: False, on_frame=None):
    """Thumbnails for many times with ONE ffmpeg process per batch: every time is a separate (cheap, keyframe)
    seek into the file and the frames come out as one PNG stream.  on_frame(index, base64_png) is called as
    soon as a frame is ready.  Returns the number of frames delivered."""
    done = 0
    for b0 in range(0, len(times), THUMB_BATCH):
        if stop():
            return done
        batch = times[b0:b0 + THUMB_BATCH]
        cmd = [FFMPEG, "-v", "error", "-nostdin"]
        parts, labs = [], []
        for i, t in enumerate(batch):
            cmd += ["-noaccurate_seek", "-ss", f"{t:.3f}", "-t", "2", "-i", path]
            vf = f"trim=end_frame=1,setpts=PTS-STARTPTS,scale={w}:{h}:force_original_aspect_ratio=decrease"
            if trc:
                vf += "," + tonemap_chain(trc)
            parts.append(f"[{i}:v:0]{vf},format=rgb24[v{i}]")
            labs.append(f"[v{i}]")
        parts.append("".join(labs) + f"concat=n={len(batch)}:v=1:a=0[o]")
        cmd += ["-filter_complex", ";".join(parts), "-map", "[o]", "-fps_mode", "passthrough",
                "-f", "image2pipe", "-vcodec", "png", "-"]
        got = 0
        try:
            proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                    stdin=subprocess.DEVNULL, creationflags=NO_WINDOW)
            buf = b""
            try:
                while True:
                    chunk = proc.stdout.read1(65536)
                    if not chunk:
                        break
                    buf += chunk
                    while True:
                        k = buf.find(PNG_END)
                        if k < 0:
                            break
                        img, buf = buf[:k + 8], buf[k + 8:]
                        if on_frame and got < len(batch):
                            on_frame(b0 + got, base64.b64encode(img))
                        got += 1
                    if stop():
                        break
            finally:
                if proc.poll() is None:
                    proc.kill()
                proc.stdout.close()
                proc.wait()
        except OSError:
            pass
        done += got
        for j in range(got, len(batch)):                # fallback: whatever the batch run did not deliver
            if stop():
                return done
            data = grab_frame(path, batch[j], w, h, None, trc)
            if data:
                if on_frame:
                    on_frame(b0 + j, data)
                done += 1
    return done


def unique_path(p):
    if not os.path.exists(p):
        return p
    base, ext = os.path.splitext(p)
    i = 2
    while os.path.exists(f"{base} ({i}){ext}"):
        i += 1
    return f"{base} ({i}){ext}"


CODECS = ["H.264 (h264_nvenc)", "HEVC (hevc_nvenc)"]


def codec_key(label):
    return "hevc_nvenc" if "HEVC" in label else "h264_nvenc"


def codec_short(label):
    return "HEVC" if "HEVC" in label else "H.264"


def video_codec_args(codec, v_bps):
    kb = max(20, int(v_bps / 1000))
    a = ["-c:v", codec, "-preset", "p5", "-tune", "hq", "-rc", "vbr",
         "-b:v", f"{kb}k", "-maxrate", f"{kb}k", "-bufsize", f"{kb * 2}k",
         "-spatial-aq", "1", "-pix_fmt", "yuv420p"]
    if codec == "hevc_nvenc":
        a += ["-tag:v", "hvc1"]
    return a


MAX_AUDIO = 4                         # max. number of audio tracks that are kept
MIX = "amix=inputs={n}:duration=longest:normalize=0,alimiter=limit=0.97:level=0"
SPEED_MIN, SPEED_MAX = 0.1, 100.0
MIN_SECTION = 0.2        # seconds


def clamp_speed(v):
    return max(SPEED_MIN, min(SPEED_MAX, v))


def build_pieces(start, end, sections):
    """Splits [start, end] into (a, b, speed) pieces that follow the speed sections (1x elsewhere)."""
    secs = sorted((max(x["a"], start), min(x["b"], end), x["speed"])
                  for x in sections if x["b"] > start and x["a"] < end)
    pieces, pos = [], start
    for a, b, sp in secs:
        a = max(a, pos)
        if b - a < 0.02:
            continue
        if a - pos > 0.001:
            pieces.append((pos, a, 1.0))
        pieces.append((a, b, sp))
        pos = b
    if end - pos > 0.001:
        pieces.append((pos, end, 1.0))
    return pieces or [(start, end, 1.0)]


def pieces_duration(pieces):
    return sum((b - a) / sp for a, b, sp in pieces)


def is_plain(pieces):
    return not pieces or (len(pieces) == 1 and abs(pieces[0][2] - 1.0) < 1e-6)


def get_pieces(c):
    return c.get("pieces") or [(c["start"], c["end"], 1.0)]


def out_to_src(pieces, t_out):
    """Time in the edited output -> time in the source clip."""
    acc = 0.0
    for a, b, sp in pieces:
        d = (b - a) / sp
        if t_out < acc + d:
            return a + (t_out - acc) * sp
        acc += d
    return pieces[-1][1]


ZOOM_FX = ["Zoom motion", "No effect (instant)"]
ZOOM_AUDIO = ["Whoosh sound", "Mute"]
ZOOM = {"motion": True, "len": 0.4, "sound": True, "vol": 80, "play": []}   # play = zooms of the preview clip
ZOOM_MIN_W = 0.1                      # smallest zoom area (fraction of the picture width) = 10x


def src_to_out(pieces, t):
    """Time in the source clip -> time in the edited output."""
    acc = 0.0
    for a, b, sp in pieces:
        if t <= a:
            return acc
        if t < b:
            return acc + (t - a) / sp
        acc += (b - a) / sp
    return acc


def zoom_times(zooms, pieces):
    """[(out_start, out_end, zoom)] of the zoom sections (zoom = (a, b, x, y, w) in source seconds)."""
    res = []
    if not zooms or not pieces:
        return res
    st, en = pieces[0][0], pieces[-1][1]
    for z in zooms:
        a, b = max(z[0], st), min(z[1], en)
        if b - a < 0.05:
            continue
        oa, ob = src_to_out(pieces, a), src_to_out(pieces, b)
        if ob - oa >= 0.05:
            res.append((oa, ob, z))
    return res


def zoom_spans(zooms, pieces, cw0, ch0, W, H):
    """Zoom windows in the output frame: [(out_start, out_end, left, top, width)], all as fractions of the
    frame.  (cw0, ch0) = size of the picture the user drew the area on (the cropped clip)."""
    f = min(W / cw0, H / ch0)
    cw, ch = cw0 * f, ch0 * f
    ox, oy = (W - cw) / 2, (H - ch) / 2
    res = []
    for oa, ob, (a, b, x, y, w) in zoom_times(zooms, pieces):
        wd = min(1.0, max(ZOOM_MIN_W * 0.5, w * cw / W))
        cx = (ox + (x + w / 2) * cw) / W
        cy = (oy + (y + w / 2) * ch) / H
        res.append((oa, ob, min(max(cx - wd / 2, 0.0), 1 - wd), min(max(cy - wd / 2, 0.0), 1 - wd), wd))
    return res


def zoom_vfilter(spans, W, H):
    """Video filters that zoom into the windows (smooth ease in / out when the motion effect is on).
    Needs the frames to start at t = 0 (the setpts at the start does that)."""
    motion, T = ZOOM["motion"], max(0.1, float(ZOOM["len"]))
    ex, lf, tp, wd = [], [], [], ["1"]
    for oa, ob, l, t_, w_ in spans:
        if motion:
            Tm = max(0.05, min(T, (ob - oa) / 2))
            k = f"clip(min(t-{oa:.3f},{ob:.3f}-t)/{Tm:.3f},0,1)"
            e = f"({k}*{k}*(3-2*{k}))"
        else:
            e = f"between(t,{oa:.3f},{ob:.3f})"
        lf.append(f"{e}*{l:.5f}")
        tp.append(f"{e}*{t_:.5f}")
        wd.append(f"{e}*({w_ - 1:.5f})")
    WD, LEFT, TOP = "(" + "+".join(wd) + ")", "(" + "+".join(lf) + ")", "(" + "+".join(tp) + ")"
    return (f"setpts=PTS-STARTPTS,scale=w='trunc({W}/{WD}/2)*2':h='trunc({H}/{WD}/2)*2':eval=frame,"
            f"crop={W}:{H}:x='max(0,min({LEFT}*{W}/{WD},{W}/{WD}-{W}-2))':"
            f"y='max(0,min({TOP}*{H}/{WD},{H}/{WD}-{H}-2))'")


def zoom_whoosh_parts(a_in, a_out, times, rate, tag):
    """[a_in] + a short whoosh when each zoom starts (rising) and ends (falling) -> [a_out]."""
    vol = max(0, min(200, int(ZOOM["vol"]))) / 100.0
    if not times or vol <= 0:
        return [f"[{a_in}]anull[{a_out}]"]
    parts, labs = [], []
    n = 0
    for oa, ob, _z in times:
        L = min(0.6 if not ZOOM["motion"] else max(0.3, min(float(ZOOM["len"]), 0.8)), (ob - oa) / 2)
        for pos, (f0, f1) in ((oa, (250, 1500)), (ob - L, (1500, 250))):
            expr = (f"(0.13*(random(0)*2-1)+0.065*sin(2*PI*({f0}*t+({f1}-{f0})*t*t/(2*{L:.3f}))))"
                    f"*pow(sin(PI*t/{L:.3f}),2)")
            ms = max(0, int(pos * 1000))
            parts.append(f"aevalsrc='{expr}|{expr}':s={rate}:d={L:.3f},highpass=f=200,lowpass=f=5500,"
                         f"aformat=sample_fmts=fltp:channel_layouts=stereo,volume={vol:.2f},"
                         f"adelay={ms}|{ms}[{tag}w{n}]")
            labs.append(f"[{tag}w{n}]")
            n += 1
    parts.append(f"[{a_in}]" + "".join(labs) + f"amix=inputs={n + 1}:duration=first:normalize=0[{a_out}]")
    return parts


def atempo_chain(sp):
    f = []
    while sp < 0.5:                      # atempo only goes down to 0.5 per stage
        f.append("atempo=0.5")
        sp /= 0.5
    while sp > 2.0:
        f.append("atempo=2.0")
        sp /= 2.0
    f.append(f"atempo={sp:.5f}")
    return f


FAST_AUDIO = ["VHS effect with sound", "Normal sped-up sound", "Mute"]
SLOW_AUDIO = ["Slow-mo effect with sound", "Normal slow-mo sound", "Mute"]
SLOW_PITCH = ["Slows down with the video (deep)", "Stays the same (stretched)"]
FAST_PITCH = ["Speeds up with the video (high)", "Stays the same (compressed)"]
AUD = {"fast": "fx", "slow": "fx", "deep": True, "high": False, "fast_vol": 70, "slow_vol": 150}      # what the sound does in sped-up / slow-motion sections: fx | normal | mute
def aud_key(label):
    return "fx" if label in (FAST_AUDIO[0], SLOW_AUDIO[0]) else ("normal" if label in (FAST_AUDIO[1], SLOW_AUDIO[1]) else "mute")
VHS = {"on": False}           # "VHS" look of the speed-up sections
SLOW = {"blend": True, "rife": False}   # slow motion: blend frames (preview + fallback) / RIFE at export
SLOW_INTERP = ["Repeat frames", "Frame blending", "RIFE AI (smoothest, needs a GPU)"]
RIFE_DIR = os.path.join(APP_DIR, "rife")
RIFE_URL = "https://github.com/nihui/rife-ncnn-vulkan/releases/download/20221029/rife-ncnn-vulkan-20221029-windows.zip"


RIFE_KEEP = ("rife-ncnn-vulkan.exe", "rife-ncnn-vulkan", "vcomp140.dll", "license", "rife-v4.6/")   # the parts the app uses


class RangeFile:
    """Read-only, seekable view of a file on a web server (HTTP range requests), so that zipfile can pull single
    members out of a big zip without downloading the whole thing."""

    BLOCK = 1 << 20

    def __init__(self, url, progress=None):
        import urllib.request
        self.progress = progress
        self.got = 0
        self.pos = 0
        self.buf, self.buf_at = b"", 0
        req = urllib.request.Request(url, headers={"User-Agent": "ClipShrinker", "Range": "bytes=0-0"})
        with urllib.request.urlopen(req, timeout=30) as r:
            self.url = r.geturl()                       # final address after the redirect
            cr = r.headers.get("Content-Range", "")
            if r.status != 206 or "/" not in cr:
                raise OSError("the server does not support partial downloads")
            self.size = int(cr.rsplit("/", 1)[1])

    def seekable(self):
        return True

    def tell(self):
        return self.pos

    def seek(self, off, whence=0):
        self.pos = off if whence == 0 else (self.pos + off if whence == 1 else self.size + off)
        return self.pos

    def _fetch(self, a, n):
        import urllib.request
        b = min(self.size, a + n) - 1
        req = urllib.request.Request(self.url, headers={"User-Agent": "ClipShrinker", "Range": f"bytes={a}-{b}"})
        with urllib.request.urlopen(req, timeout=60) as r:
            data = r.read()
        self.got += len(data)
        if self.progress:
            self.progress(self.got)
        return data

    def read(self, n=-1):
        if n < 0 or self.pos + n > self.size:
            n = self.size - self.pos
        out = []
        while n > 0:
            if not (self.buf_at <= self.pos < self.buf_at + len(self.buf)):
                self.buf_at = self.pos
                self.buf = self._fetch(self.pos, max(n, 65536 if self.size - self.pos < 4 * self.BLOCK else self.BLOCK))
                if not self.buf:
                    break
            o = self.pos - self.buf_at
            chunk = self.buf[o:o + n]
            out.append(chunk)
            self.pos += len(chunk)
            n -= len(chunk)
        return b"".join(out)


# ---- rebindable shortcuts (trimming and the effects)
KEY_DEFAULTS = {"trim_start": "1", "trim_end": "2", "speed": "s", "slow": "d", "zoom": "z", "effects": "f2"}
KEY_TEXT = {"trim_start": "Set the trim start at the playhead", "trim_end": "Set the trim end at the playhead",
            "speed": "Add a speed-up section (blue) at the playhead", "slow": "Add a slow-down section (pink) at the playhead",
            "zoom": "Add a zoom section (white) at the playhead", "effects": "Open effect settings"}
MOD_KEYSYMS = {"Shift_L", "Shift_R", "Control_L", "Control_R", "Alt_L", "Alt_R", "Meta_L", "Meta_R", "Caps_Lock",
               "Num_Lock", "Scroll_Lock", "Super_L", "Super_R", "ISO_Level3_Shift", "Win_L", "Win_R"}
RESERVED_PLAIN = {"space", "f1", "delete", "backspace", "escape", "return", "tab", "m"}
RESERVED_CTRL = {"ctrl+o", "ctrl+s", "ctrl+shift+s", "ctrl+z", "ctrl+y", "ctrl+shift+z", "ctrl+a", "ctrl+e",
                 "ctrl+shift+e"}
KEY_SHOWN = {"exclam": "!", "at": "@", "numbersign": "#", "dollar": "$", "percent": "%", "asciicircum": "^",
             "ampersand": "&", "asterisk": "*", "parenleft": "(", "parenright": ")", "minus": "-", "plus": "+",
             "equal": "=", "comma": ",", "period": ".", "slash": "/", "backslash": "\\", "semicolon": ";",
             "apostrophe": "'", "bracketleft": "[", "bracketright": "]", "grave": "`", "space": "Space",
             "return": "Enter", "tab": "Tab", "prior": "Page Up", "next": "Page Down", "escape": "Esc"}


def chord_from_event(e):
    """'ctrl+alt+shift+key' text of a key event (None for a bare modifier key)."""
    k = e.keysym or ""
    if not k or k in MOD_KEYSYMS:
        return None
    if k.startswith("KP_") and k[3:].isdigit():
        k = k[3:]
    k = k.lower()
    st = e.state
    mods = []
    if st & 0x4:
        mods.append("ctrl")
    if st & (0x20000 if os.name == "nt" else 0x8):
        mods.append("alt")
    if st & 0x1:
        mods.append("shift")
    return "+".join(mods + [k])


def chord_label(ch):
    parts = ch.split("+")
    key = parts[-1]
    shown = KEY_SHOWN.get(key, key.upper() if len(key) == 1 else (key.upper() if len(key) <= 3 else key.title()))
    return " + ".join([{"ctrl": "Ctrl", "alt": "Alt", "shift": "Shift"}[m] for m in parts[:-1]] + [shown])


def clean_suffix(txt):
    t = "".join(c for c in (txt or "") if c not in '<>:"/\\|?*' and ord(c) >= 32).strip().strip("._ ")
    return t


def interp_key(label):
    return "rife" if label == SLOW_INTERP[2] else ("blend" if label == SLOW_INTERP[1] else "repeat")


def rife_exe():
    p = os.path.join(RIFE_DIR, "rife-ncnn-vulkan.exe" if os.name == "nt" else "rife-ncnn-vulkan")
    return p if os.path.isfile(p) else None


def rife_model():
    """The RIFE model folder to use (v4.6 if it is there, otherwise any v4 model)."""
    try:
        names = sorted(n for n in os.listdir(RIFE_DIR) if n.startswith("rife-v4") and
                       os.path.isfile(os.path.join(RIFE_DIR, n, "flownet.param")))
    except OSError:
        return None
    if not names:
        return None
    return os.path.join(RIFE_DIR, "rife-v4.6" if "rife-v4.6" in names else names[-1])


def rife_ready():
    return bool(rife_exe() and rife_model())
VHS_MIN = 0.15                # sections shorter than this (after the speed change, seconds) get no effect


def vhs_video_parts(i, o, D, size):
    """Filter parts: stream [i] (one section, already cut and re-timed, D seconds long) -> [o] with a VHS look:
    constant light noise / colour bleed / rolling tracking band, and a stronger glitch burst at its start and end."""
    B = min(0.35, D / 2)
    en = f"enable='lt(t,{B:.3f})+gt(t,{D - B:.3f})'"
    chain = ["noise=alls=10:allf=t", "rgbashift=rh=-2:bh=2", "hue=s=1.2",
             f"noise=alls=60:allf=t+u:{en}", f"rgbashift=rh=-9:bh=9:{en}", f"hue=s=0.5:{en}",
             f"lutyuv=y='val+16':{en}"]
    if not size:
        return [f"[{i}]" + ",".join(chain) + f",format=yuv420p[{o}]"]
    W, H = size
    bh = max(8, int(H * 0.05))
    return [f"[{i}]" + ",".join(chain) + f"[{o}_f]",
            f"color=c=white:s={W}x{bh}:r=30:d={D + 0.5:.3f},format=rgba,colorchannelmixer=aa=0.16[{o}_k]",
            f"[{o}_f][{o}_k]overlay=x=0:y='mod(t*{H * 0.45:.0f},{H + bh})-{bh}':eof_action=pass:format=yuv420[{o}]"]


def seg_video(src, pieces, t_base, tag, out, size=None, fps=None, rife=None):
    """Filter-graph parts that cut stream [src] into pieces, change their speed and join them again.
    Times in `pieces` are source seconds; the input stream starts at t_base (= clip start).
    size = (w, h) of the stream (needed for the VHS tracking band)."""
    def one(a, b, sp, i, o, j=0):
        if rife and j in rife:                               # slow motion rendered by RIFE (extra input)
            idx, fw, fh = rife[j]
            sc = f",scale={size[0]}:{size[1]}:flags=lanczos,setsar=1" if size and (fw, fh) != tuple(size) else ""
            return [f"[{i}]nullsink", f"[{idx}:v:0]setpts=PTS-STARTPTS{sc},format=yuv420p[{o}]"]
        base = (f"[{i}]trim=start={a - t_base:.4f}:end={b - t_base:.4f},"
                f"setpts=(PTS-STARTPTS)/{sp:.5f}")
        D = (b - a) / sp
        if sp < 1.0 and SLOW["blend"] and fps:               # slow motion: blend neighbouring frames
            fr = Fraction(fps).limit_denominator(1001)
            base += f",framerate=fps={fr.numerator}/{fr.denominator}:interp_start=0:interp_end=255:scene=100"
        if VHS["on"] and sp > 1.0 and D >= VHS_MIN:
            return [base + f"[{o}_r]"] + vhs_video_parts(f"{o}_r", o, D, size)
        return [base + f"[{o}]"]
    k = len(pieces)
    if k == 1:
        return one(*pieces[0], src, out, 0)
    parts = [f"[{src}]split={k}" + "".join(f"[{tag}s{j}]" for j in range(k))]
    for j, (a, b, sp) in enumerate(pieces):
        parts += one(a, b, sp, f"{tag}s{j}", f"{tag}p{j}", j)
    parts.append("".join(f"[{tag}p{j}]" for j in range(k)) + f"concat=n={k}:v=1:a=0[{out}]")
    return parts


def vhs_audio_parts(m, o, D, rate):
    """Section audio [m] + a tape-wind burst at its start (rising) and at its end (falling)."""
    L = min(0.45, D / 2)
    fo = max(0.0, L - 0.12)
    ms = int((D - L) * 1000)
    parts = []
    for n, (ph, dl) in enumerate(((f"220*t+2600*t*t/{L:.3f}", ""),
                                  (f"2800*t-1300*t*t/{L:.3f}", f",adelay={ms}|{ms}"))):
        expr = f"0.214*sin(2*PI*({ph}))"
        parts += [f"anoisesrc=d={L:.3f}:c=pink:r={rate}:a=0.34,highpass=f=250,lowpass=f=7000,"
                  f"aformat=sample_fmts=fltp:channel_layouts=stereo[{o}_n{n}]",
                  f"aevalsrc='{expr}|{expr}':s={rate}:d={L:.3f},aformat=sample_fmts=fltp:channel_layouts=stereo"
                  f"[{o}_t{n}]",
                  f"[{o}_n{n}][{o}_t{n}]amix=inputs=2:normalize=0,afade=t=in:d=0.03,"
                  f"afade=t=out:st={fo:.3f}:d=0.12{dl}[{o}_b{n}]"]
    parts.append(f"[{m}][{o}_b0][{o}_b1]amix=inputs=3:duration=first:normalize=0[{o}]")
    return parts


def slow_audio_parts(m, o, D, rate):
    """Section audio [m] + a deep 'bullet time' effect: a sub thump and a falling rumble when it starts,
    a rising swell when it speeds back up at the end."""
    L = min(0.6, D / 2)
    ms = int((D - L) * 1000)
    ph0 = f"260*t-215*t*t/(2*{L:.3f})"                      # 260 Hz -> 45 Hz
    ph1 = f"45*t+215*t*t/(2*{L:.3f})"                       # 45 Hz -> 260 Hz
    e0 = f"0.12*sin(2*PI*({ph0}))*exp(-3*t/{L:.3f})+0.18*sin(2*PI*48*t)*exp(-9*t)"
    e1 = f"0.13*sin(2*PI*({ph1}))*sin(PI*t/{L:.3f})"
    parts = []
    for n, (expr, dl, nz) in enumerate(((e0, "", "lowpass=f=500,afade=t=out:d=%.3f" % L),
                                        (e1, f",adelay={ms}|{ms}", "lowpass=f=700,afade=t=in:d=%.3f" % L))):
        parts += [f"aevalsrc='{expr}|{expr}':s={rate}:d={L:.3f},aformat=sample_fmts=fltp:channel_layouts=stereo[{o}_t{n}]",
                  f"anoisesrc=d={L:.3f}:c=brown:r={rate}:a=0.09,{nz},aformat=sample_fmts=fltp:channel_layouts=stereo[{o}_n{n}]",
                  f"[{o}_t{n}][{o}_n{n}]amix=inputs=2:normalize=0,afade=t=in:d=0.01,afade=t=out:st={max(0.0, L - 0.08):.3f}:d=0.08{dl}[{o}_b{n}]"]
    parts.append(f"[{m}][{o}_b0][{o}_b1]amix=inputs=3:duration=first:normalize=0[{o}]")
    return parts


def seg_audio(src, pieces, t_base, tag, out, mute_fast, rate=48000):
    def one(a, b, sp, i, o):
        f = [f"atrim=start={a - t_base:.4f}:end={b - t_base:.4f}", "asetpts=PTS-STARTPTS"]
        if abs(sp - 1.0) > 1e-6:
            mode = AUD["fast"] if sp > 1.0 else AUD["slow"]
            if sp < 1.0 and AUD["deep"] and mode != "mute":  # "classic" slow down: the pitch drops with the speed
                f += [f"asetrate={max(1000, int(round(rate * sp)))}", f"aresample={rate}"]
            elif sp > 1.0 and AUD.get("high") and mode != "mute":   # "classic" speed-up: the pitch rises with the speed
                f += [f"asetrate={int(round(rate * sp))}", f"aresample={rate}"]
            else:
                f += atempo_chain(sp)
            if mode == "mute":
                f.append("volume=0")
        D = (b - a) / sp
        vol = 1.0
        if abs(sp - 1.0) > 1e-6:
            vol = max(0, min(200, int(AUD.get("fast_vol" if sp > 1.0 else "slow_vol", 100)))) / 100.0
        vf = f"volume={vol:.2f}" if abs(vol - 1.0) > 1e-6 else None
        if abs(sp - 1.0) > 1e-6 and D >= VHS_MIN:
            mode = AUD["fast"] if sp > 1.0 else AUD["slow"]
            if mode == "fx":
                fn = vhs_audio_parts if sp > 1.0 else slow_audio_parts
                if vf:
                    return ([f"[{i}]" + ",".join(f) + f"[{o}_m]"] + fn(f"{o}_m", f"{o}_x", D, rate) +
                            [f"[{o}_x]{vf}[{o}]"])
                return [f"[{i}]" + ",".join(f) + f"[{o}_m]"] + fn(f"{o}_m", o, D, rate)
        if vf:
            f.append(vf)
        return [f"[{i}]" + ",".join(f) + f"[{o}]"]
    k = len(pieces)
    if k == 1:
        return one(*pieces[0], src, out)
    parts = [f"[{src}]asplit={k}" + "".join(f"[{tag}a{j}]" for j in range(k))]
    for j, (a, b, sp) in enumerate(pieces):
        parts += one(a, b, sp, f"{tag}a{j}", f"{tag}q{j}")
    parts.append("".join(f"[{tag}q{j}]" for j in range(k)) + f"concat=n={k}:v=0:a=1[{out}]")
    return parts


def clip_naudio(c):
    return c.get("naudio", 1 if c.get("audio") else 0)


def out_tracks(clips, mix=False):
    """Number of audio tracks in the output file."""
    m = max(clip_naudio(c) for c in clips)
    return 0 if m == 0 else (1 if mix else min(MAX_AUDIO, m))


def clip_vfilters(c, W, H, ratio, tonemap):
    """Per-clip video filters before the speed sections: crop, scale + pad to W x H, HDR -> SDR."""
    f = []
    cw, ch = crop_size(c["w"], c["h"], ratio)
    if (cw, ch) != (c["w"], c["h"]):
        f.append(f"crop={cw}:{ch}")                           # centered by default
    f.append(f"scale={W}:{H}:force_original_aspect_ratio=decrease:flags=lanczos")
    f.append(f"pad={W}:{H}:(ow-iw)/2:(oh-ih)/2")
    f.append("setsar=1")
    if tonemap and c.get("trc"):                              # HDR clip -> SDR (after scaling = cheaper)
        f.append(tonemap_chain(c["trc"]))
    return f


def build_graph(clips, W, H, ratio, xfade, T, fps, tonemap=False, mute_fast=True, mix=False, rife=None):
    """Builds the ffmpeg filter_complex for one or more clips.
    Returns (graph, video_label, [audio labels] or None)."""
    n = len(clips)
    n_out = out_tracks(clips, mix)
    any_audio = n_out > 0
    all_pieces = [get_pieces(c) for c in clips]
    durs = [pieces_duration(p) for p in all_pieces]          # lengths after speed changes
    parts = []
    for i, c in enumerate(clips):
        pieces = all_pieces[i]
        seg = not is_plain(pieces)
        src_dur = c["end"] - c["start"]
        f = clip_vfilters(c, W, H, ratio, tonemap)
        tail = []
        cw0, ch0 = crop_size(c["w"], c["h"], ratio)
        zs = zoom_spans(c.get("zooms"), pieces, cw0, ch0, W, H)
        if zs:
            tail.append(zoom_vfilter(zs, W, H))
        if n > 1 or seg:                                      # common frame rate (joining / after speed-up)
            fr = Fraction(fps).limit_denominator(1001)
            tail.append(f"fps={fr.numerator}/{fr.denominator}")
        tail += ["format=yuv420p", "settb=AVTB", "setpts=PTS-STARTPTS"]
        if seg:
            parts.append(f"[{i}:v:0]" + ",".join(f + ["setpts=PTS-STARTPTS"]) + f"[b{i}]")
            parts += seg_video(f"b{i}", pieces, c["start"], f"c{i}", f"m{i}", (W, H), fps, (rife or {}).get(i))
            parts.append(f"[m{i}]" + ",".join(tail) + f"[v{i}]")
        else:
            parts.append(f"[{i}:v:0]" + ",".join(f + tail) + f"[v{i}]")
        if any_audio:
            na = clip_naudio(c)
            streams = []                                      # one label per source audio track
            for k in range(na):
                lbl = f"ab{i}_{k}"
                parts.append(f"[{i}:a:{k}]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,"
                             f"apad=whole_dur={src_dur:.3f},atrim=duration={src_dur:.3f},"
                             f"asetpts=PTS-STARTPTS[{lbl}]")
                streams.append(lbl)
            if mix and len(streams) > 1:                      # game + microphone -> one track
                parts.append("".join(f"[{x}]" for x in streams) + MIX.format(n=len(streams)) + f"[abm{i}]")
                streams = [f"abm{i}"]
            zt = zoom_times(c.get("zooms"), pieces) if ZOOM["sound"] else []
            for t in range(n_out):
                if t < len(streams):
                    lab = f"a{i}_{t}z" if (zt and t == 0) else f"a{i}_{t}"
                    if seg:
                        parts += seg_audio(streams[t], pieces, c["start"], f"d{i}_{t}", lab, mute_fast)
                    else:
                        parts.append(f"[{streams[t]}]anull[{lab}]")
                    if zt and t == 0:
                        parts += zoom_whoosh_parts(lab, f"a{i}_{t}", zt, 48000, f"zw{i}")
                else:                                         # silence for missing tracks / clips without audio
                    parts.append(f"anullsrc=r=48000:cl=stereo,atrim=duration={durs[i]:.3f},"
                                 f"asetpts=PTS-STARTPTS[a{i}_{t}]")
    if n == 1:
        return ";".join(parts), "v0", ([f"a0_{t}" for t in range(n_out)] if any_audio else None)
    if not xfade:
        ins = "".join(f"[v{i}]" + "".join(f"[a{i}_{t}]" for t in range(n_out)) for i in range(n))
        outs = "[vout]" + "".join(f"[aout{t}]" for t in range(n_out))
        parts.append(f"{ins}concat=n={n}:v=1:a={n_out}{outs}")
        return ";".join(parts), "vout", ([f"aout{t}" for t in range(n_out)] if any_audio else None)
    cum, prev = durs[0], "v0"
    for i in range(1, n):
        out = f"vx{i}"
        if xfade == GLITCH:
            parts.append(f"[{prev}][v{i}]xfade=transition=fadefast:duration={T:.3f}:offset={cum - T:.3f}[{out}g]")
            parts += glitch_parts(f"{out}g", out, cum - T, T, W, H, f"gl{i}", seed=i * 31 + 7)
        else:
            parts.append(f"[{prev}][v{i}]xfade=transition={xfade}:duration={T:.3f}:offset={cum - T:.3f}[{out}]")
        prev, cum = out, cum + durs[i] - T
    vlast, alast = prev, None
    if any_audio:
        alast = []
        for t in range(n_out):
            prev = f"a0_{t}"
            for i in range(1, n):
                out = f"ax{i}_{t}"
                parts.append(f"[{prev}][a{i}_{t}]acrossfade=d={T:.3f}:c1=tri:c2=tri[{out}]")
                prev = out
            alast.append(prev)
    return ";".join(parts), vlast, alast


def read_exact(f, n):
    """Read exactly n bytes from a pipe, or None at end of stream."""
    chunks, got = [], 0
    while got < n:
        b = f.read(n - got)
        if not b:
            return None
        chunks.append(b)
        got += len(b)
    return b"".join(chunks)


def play_command(path, start, dur, crop, w, h, fps, hwdec, trc=None, pieces=None):
    """ffmpeg command that streams raw RGB frames (the player paces them itself).
    pieces: speed sections (source seconds); the input is cut at `start`."""
    cmd = [FFMPEG, "-v", "error", "-nostdin"]
    zs = zoom_spans(ZOOM["play"], pieces or [(start, start + dur, 1.0)], crop[0] if crop else w,
                    crop[1] if crop else h, w, h) if ZOOM["play"] else []
    gpu = hwdec == 2 and is_plain(pieces) and not crop and not trc and not zs   # decode AND scale on the GPU
    if gpu:
        cmd += ["-hwaccel", "cuda", "-hwaccel_output_format", "cuda"]
    elif hwdec:
        cmd += ["-hwaccel", "cuda"]
    cmd += ["-probesize", "5000000", "-analyzeduration", "1000000",
            "-ss", f"{start:.3f}", "-t", f"{dur:.3f}", "-i", path, "-an"]
    if gpu:
        cmd += ["-vf", f"scale_cuda={w}:{h}:format=nv12,hwdownload,format=nv12,fps={fps:g}"]
    elif is_plain(pieces) and not zs:
        vf = (f"crop={crop[0]}:{crop[1]}," if crop else "") + f"fps={fps:g},scale={w}:{h}:flags=fast_bilinear"
        if trc:
            vf += "," + tonemap_chain(trc)
        cmd += ["-vf", vf]
    else:                                  # scale first (cheap), then cut into pieces and change speed
        base = ([f"crop={crop[0]}:{crop[1]}"] if crop else []) + [f"scale={w}:{h}:flags=fast_bilinear"]
        if trc:
            base.append(tonemap_chain(trc))
        zf = (zoom_vfilter(zs, w, h) + ",") if zs else ""
        graph = ["[0:v:0]" + ",".join(base + ["setpts=PTS-STARTPTS"]) + "[pb]"] + \
                seg_video("pb", pieces or [(start, start + dur, 1.0)], start, "q", "pm", (w, h), fps) + \
                [f"[pm]{zf}fps={fps:g}[pout]"]
        cmd += ["-filter_complex", ";".join(graph), "-map", "[pout]"]
    cmd += ["-f", "rawvideo", "-pix_fmt", "rgb24", "-"]
    return cmd


def preview_audio_cmd(path, start, dur, pieces=None, mute_fast=True, naudio=1, out=None):
    """ffmpeg command for the preview audio: a WAV file (out=path) or raw 44.1 kHz stereo s16le on stdout."""
    cmd = [FFMPEG, "-y", "-v", "error", "-nostdin", "-probesize", "5000000", "-analyzeduration", "1000000",
           "-ss", f"{start:.3f}", "-t", f"{dur:.3f}", "-i", path, "-vn"]
    naudio = max(1, naudio)
    zt = zoom_times(ZOOM["play"], pieces or [(start, start + dur, 1.0)]) if (ZOOM["play"] and ZOOM["sound"]) else []
    if is_plain(pieces) and naudio == 1 and not zt:
        cmd += ["-map", "0:a:0"]
    else:
        parts, labs = [], []
        for k in range(naudio):
            parts.append(f"[0:a:{k}]aresample=44100,aformat=channel_layouts=stereo,apad=whole_dur={dur:.3f},"
                         f"atrim=duration={dur:.3f},asetpts=PTS-STARTPTS[p{k}]")
            labs.append(f"[p{k}]")
        if naudio > 1:
            parts.append("".join(labs) + MIX.format(n=naudio) + "[ab]")
        else:
            parts[-1] = parts[-1].replace("[p0]", "[ab]")
        fin = "aoz" if zt else "aout"
        if is_plain(pieces):
            parts.append(f"[ab]anull[{fin}]")
        else:
            parts += seg_audio("ab", pieces, start, "w", fin, mute_fast, 44100)
        if zt:
            parts += zoom_whoosh_parts("aoz", "aout", zt, 44100, "zp")
        cmd += ["-filter_complex", ";".join(parts), "-map", "[aout]"]
    cmd += ["-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le"]
    cmd += [out] if out else ["-f", "s16le", "-"]
    return cmd


def effect_sample_cmd(kind, path, t0, out, rate=44100):
    """ffmpeg command for a short sample of a sound effect (Settings > play button):
    normal sound, then the effect (sped-up / slow-mo section or a zoom whoosh), then normal again.
    path=None -> a little synthetic melody is used as the 'clip sound'."""
    if kind == "fast":
        pieces = [(t0, t0 + 1.2, 1.0), (t0 + 1.2, t0 + 4.8, 3.0), (t0 + 4.8, t0 + 5.8, 1.0)]
    elif kind == "slow":
        pieces = [(t0, t0 + 1.2, 1.0), (t0 + 1.2, t0 + 2.2, 0.5), (t0 + 2.2, t0 + 3.2, 1.0)]
    else:
        pieces = [(t0, t0 + 3.4, 1.0)]
    src_dur = pieces[-1][1] - t0
    cmd = [FFMPEG, "-y", "-v", "error", "-nostdin"]
    if path:
        cmd += ["-ss", f"{t0:.3f}", "-t", f"{src_dur:.3f}", "-i", path]
        base = (f"[0:a:0]aresample={rate},aformat=sample_fmts=fltp:channel_layouts=stereo,"
                f"apad=whole_dur={src_dur:.3f},atrim=duration={src_dur:.3f},asetpts=PTS-STARTPTS[ab]")
    else:
        note = "220*pow(2,mod(floor(t*4)*5,12)/12)"
        expr = f"0.25*sin(2*PI*t*{note})*exp(-mod(t,0.25)*6)"
        base = (f"aevalsrc='{expr}|{expr}':s={rate}:d={src_dur:.3f},"
                f"aformat=sample_fmts=fltp:channel_layouts=stereo[ab]")
    parts = [base]
    if kind == "zoom":
        parts += zoom_whoosh_parts("ab", "eo", [(0.8, 2.6, 2.0)], rate, "ez")
    else:
        parts += seg_audio("ab", pieces, t0, "e", "eo", True, rate)
    cmd += ["-filter_complex", ";".join(parts), "-map", "[eo]", "-ac", "2", "-ar", str(rate),
            "-c:a", "pcm_s16le", out]
    return cmd


def make_preview_wav(path, start, dur, tag, pieces=None, mute_fast=True, naudio=1):
    """Extracts the trimmed (and speed-edited) audio to a temp WAV for winsound; all tracks are mixed."""
    out = os.path.join(tempfile.gettempdir(), f"clipshrinker_{os.getpid()}_{tag}.wav")
    r = run_capture(preview_audio_cmd(path, start, dur, pieces, mute_fast, naudio, out))
    if r.returncode != 0 or not os.path.exists(out):
        return None
    _TEMP_WAVS.append(out)
    return out


APP_NAME, APP_VERSION = "ClipShrinker", "1.0.6"
SIDE_DIVIDERS = True                  # thin lines between the Export panel sections
PV = {"vol": 1.0}                     # preview volume (0..1), set by the speaker slider


def scale_pcm(data, g):
    """16-bit PCM bytes * g (preview volume)."""
    a = array.array("h")
    a.frombytes(data[:len(data) // 2 * 2])
    return array.array("h", [int(x * g) for x in a]).tobytes()


class WaveOut:
    """Gapless audio through the Windows waveOut API (winmm): ffmpeg streams PCM, which is queued in small
    buffers.  It is opened paused, pre-filled, and released with start() when the first video frame appears."""
    BLK = 17640                                    # 100 ms of 44.1 kHz stereo 16-bit
    NBUF = 10

    def __init__(self, api=None):
        self.api = api or _WinMM()
        self.stop_flag = False
        self.muted = False
        self.started = False
        self.thread = None
        self.api.open(44100, 2, 16, self.NBUF, self.BLK)
        self.api.pause()

    def feed(self, stream):
        self.thread = threading.Thread(target=self._run, args=(stream,), daemon=True)
        self.thread.start()

    def _run(self, stream):
        api = self.api
        try:
            i = 0
            while not self.stop_flag:
                data = stream.read(self.BLK)
                if not data:
                    break
                while not self.stop_flag and not api.free(i):
                    time.sleep(0.004)
                if self.stop_flag:
                    break
                if self.muted:
                    data = bytes(len(data))
                elif PV["vol"] < 0.995:
                    data = scale_pcm(data, PV["vol"])
                api.write(i, data)
                i = (i + 1) % self.NBUF
            while not self.stop_flag and not api.all_free():      # let the tail play out
                time.sleep(0.02)
        except Exception:
            if _LOG:
                traceback.print_exc(file=_LOG)
        finally:
            api.close()

    def set_muted(self, flag):
        self.muted = flag
        try:
            self.api.set_volume(0 if flag else 0xFFFF)
        except Exception:
            pass

    def start(self):
        if not self.started and not self.stop_flag:
            self.started = True
            self.api.resume()

    def stop(self):
        self.stop_flag = True
        try:
            self.api.reset()
        except Exception:
            pass


class _WinMM:
    def __init__(self):
        import ctypes
        from ctypes import wintypes
        self.ct = ctypes
        self.mm = ctypes.WinDLL("winmm")
        self.h = ctypes.c_void_p()
        self.lock = threading.Lock()
        self.closed = False

        class WAVEFORMATEX(ctypes.Structure):
            _fields_ = [("wFormatTag", wintypes.WORD), ("nChannels", wintypes.WORD),
                        ("nSamplesPerSec", wintypes.DWORD), ("nAvgBytesPerSec", wintypes.DWORD),
                        ("nBlockAlign", wintypes.WORD), ("wBitsPerSample", wintypes.WORD), ("cbSize", wintypes.WORD)]

        class WAVEHDR(ctypes.Structure):
            _fields_ = [("lpData", ctypes.c_void_p), ("dwBufferLength", wintypes.DWORD),
                        ("dwBytesRecorded", wintypes.DWORD), ("dwUser", ctypes.c_size_t),
                        ("dwFlags", wintypes.DWORD), ("dwLoops", wintypes.DWORD),
                        ("lpNext", ctypes.c_void_p), ("reserved", ctypes.c_size_t)]
        self.FMT, self.HDR = WAVEFORMATEX, WAVEHDR
        mm = self.mm
        for fn in ("waveOutPrepareHeader", "waveOutWrite", "waveOutUnprepareHeader"):
            getattr(mm, fn).argtypes = [ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT]
        for fn in ("waveOutPause", "waveOutRestart", "waveOutReset", "waveOutClose"):
            getattr(mm, fn).argtypes = [ctypes.c_void_p]
        mm.waveOutSetVolume.argtypes = [ctypes.c_void_p, wintypes.DWORD]
        mm.waveOutOpen.argtypes = [ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p,
                                   ctypes.c_size_t, ctypes.c_size_t, wintypes.DWORD]

    def open(self, rate, ch, bits, nbuf, blk):
        ct = self.ct
        f = self.FMT(1, ch, rate, rate * ch * bits // 8, ch * bits // 8, bits, 0)
        if self.mm.waveOutOpen(ct.byref(self.h), 0xFFFFFFFF, ct.byref(f), 0, 0, 0) != 0:
            raise OSError("waveOutOpen failed")
        self.bufs = [ct.create_string_buffer(blk) for _ in range(nbuf)]
        self.hdrs = []
        for b in self.bufs:
            hd = self.HDR()
            hd.lpData = ct.addressof(b)
            hd.dwBufferLength = blk
            if self.mm.waveOutPrepareHeader(self.h, ct.byref(hd), ct.sizeof(hd)) != 0:
                raise OSError("waveOutPrepareHeader failed")
            hd.dwFlags |= 1                        # WHDR_DONE: slot is free
            self.hdrs.append(hd)

    def free(self, i):
        return bool(self.hdrs[i].dwFlags & 1)

    def all_free(self):
        return all(h.dwFlags & 1 for h in self.hdrs)

    def write(self, i, data):
        ct = self.ct
        with self.lock:
            if self.closed:
                return
            ct.memmove(self.bufs[i], data, len(data))
            hd = self.hdrs[i]
            hd.dwBufferLength = len(data)
            hd.dwFlags &= ~1
            self.mm.waveOutWrite(self.h, ct.byref(hd), ct.sizeof(hd))

    def set_volume(self, v):
        self.mm.waveOutSetVolume(self.h, (v & 0xFFFF) | ((v & 0xFFFF) << 16))

    def pause(self):
        self.mm.waveOutPause(self.h)

    def resume(self):
        with self.lock:
            if not self.closed:
                self.mm.waveOutRestart(self.h)

    def reset(self):
        with self.lock:
            if not self.closed:
                self.mm.waveOutReset(self.h)

    def close(self):
        ct = self.ct
        with self.lock:
            if self.closed:
                return
            self.closed = True
            self.mm.waveOutReset(self.h)
            for hd in self.hdrs:
                self.mm.waveOutUnprepareHeader(self.h, ct.byref(hd), ct.sizeof(hd))
            self.mm.waveOutClose(self.h)


def slice_wav(src, offset, tag):
    """Copy of a WAV starting `offset` seconds in (for joining a chunk that is already running late)."""
    import wave
    out = os.path.join(tempfile.gettempdir(), f"clipshrinker_{os.getpid()}_{tag}.wav")
    with wave.open(src, "rb") as r, wave.open(out, "wb") as w:
        w.setparams(r.getparams())
        r.setpos(min(r.getnframes(), int(offset * r.getframerate())))
        w.writeframes(r.readframes(r.getnframes()))
    _TEMP_WAVS.append(out)
    return out


def cleanup_wavs():
    for p in list(_TEMP_WAVS):
        try:
            os.remove(p)
            _TEMP_WAVS.remove(p)
        except OSError:
            pass


# ------------------------------------------------------------------ encoder
class Cancelled(Exception):
    pass


class Encoder:
    """Runs ffmpeg jobs and hits a target file size. No GUI code in here."""

    def __init__(self, progress_cb=None, status_cb=None):
        self.progress_cb = progress_cb or (lambda pct: None)
        self.status_cb = status_cb or (lambda text: None)
        self.proc = None
        self.cancelled = False
        self.use_hwdec = True
        self.rife_map, self.extras, self.rife_tmp, self.rife_done = {}, [], None, False

    def cancel(self):
        self.cancelled = True
        if self.proc and self.proc.poll() is None:
            self.proc.kill()

    def _run(self, cmd, duration, label, pbase, pspan):
        self.proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     text=True, creationflags=NO_WINDOW, bufsize=1)
        tail = []
        for line in self.proc.stdout:
            line = line.strip()
            if line.startswith("out_time_us=") or line.startswith("out_time_ms="):
                try:
                    frac = min(1.0, int(line.split("=")[1]) / 1_000_000 / duration)
                    self.progress_cb(pbase + pspan * frac)
                    self.status_cb(f"{label}: {frac * 100:.0f}%")
                except ValueError:
                    pass
            elif "=" not in line:
                tail.append(line)
                tail = tail[-25:]
        self.proc.wait()
        return self.proc.returncode == 0, "\n".join(tail)

    @staticmethod
    def _color_tags(clips, tonemap):
        """Explicit output colour tags: SDR/BT.709 after tone-mapping, otherwise keep the HDR tags."""
        hdr = [c for c in clips if c.get("trc")]
        if not hdr:
            return []
        if tonemap:
            return ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
                    "-color_range", "tv"]
        c = hdr[0]
        tags = ["-color_trc", c["trc"]]
        if c.get("prim") and c["prim"] != "unknown":
            tags += ["-color_primaries", c["prim"]]
        if c.get("space") and c["space"] != "unknown":
            tags += ["-colorspace", c["space"]]
        return tags

    def _cmd(self, out, clips, graph, vl, al, codec, v_bps, audio_k, hwdec, tonemap=False):
        cmd = [FFMPEG, "-y", "-hide_banner", "-nostats", "-progress", "pipe:1"]
        for c in clips:
            if hwdec:
                cmd += ["-hwaccel", "cuda"]
            cmd += ["-ss", f"{c['start']:.3f}", "-t", f"{c['end'] - c['start']:.3f}", "-i", c["path"]]
        for d, nf, D in self.extras:                      # slow motion frames rendered by RIFE
            cmd += ["-framerate", f"{nf / D:.6f}", "-start_number", "1", "-i", os.path.join(d, "%08d.jpg")]
        cmd += ["-filter_complex", graph, "-map", f"[{vl}]"]
        for lbl in (al or []):
            cmd += ["-map", f"[{lbl}]"]
        cmd += video_codec_args(codec, v_bps) + self._color_tags(clips, tonemap)
        if al:
            cmd += ["-c:a", "aac", "-b:a", audio_k]
            if len(al) == 2:
                cmd += ["-metadata:s:a:0", "handler_name=Game audio", "-metadata:s:a:1", "handler_name=Microphone"]
        cmd += ["-movflags", "+faststart", out]
        return cmd

    def encode(self, out, clips, st, pbase=0.0, pspan=100.0, tag=""):
        self.rife_map, self.extras, self.rife_tmp, self.rife_done = {}, [], None, False
        try:
            return self._encode(out, clips, st, pbase, pspan, tag)
        finally:
            if self.rife_tmp:
                shutil.rmtree(self.rife_tmp, ignore_errors=True)
            self.rife_map, self.extras, self.rife_tmp = {}, [], None

    def _rife_prepare(self, clips, W, H, ratio, tonemap, pbase, pspan):
        """Renders every slow-motion piece with RIFE into numbered JPEG frames (extra ffmpeg inputs).
        Returns the (pbase, pspan) that is left for the real encode. Any failure -> that piece keeps the
        frame-blending fallback."""
        self.rife_done = True
        jobs = [(i, j, a, b, sp) for i, c in enumerate(clips) for j, (a, b, sp) in enumerate(get_pieces(c))
                if sp < 1.0 and (b - a) / sp >= 0.2]
        if not jobs:
            return pbase, pspan
        exe, model = rife_exe(), rife_model()
        if not (exe and model):
            self.status_cb("RIFE is not installed - using frame blending for slow motion")
            return pbase, pspan
        share = 0.35
        self.rife_tmp = tempfile.mkdtemp(prefix="clipshrinker_rife_")
        for n, (i, j, a, b, sp) in enumerate(jobs):
            if self.cancelled:
                raise Cancelled()
            c = clips[i]
            b0 = pbase + pspan * share * n / len(jobs)
            span = pspan * share / len(jobs)
            label = f"Slow motion (RIFE) {n + 1}/{len(jobs)}"
            self.status_cb(label + ": extracting frames")
            d_in, d_out = os.path.join(self.rife_tmp, f"i{n}"), os.path.join(self.rife_tmp, f"o{n}")
            os.makedirs(d_in)
            os.makedirs(d_out)
            fr = Fraction(c["fps"]).limit_denominator(1001)
            vf = ",".join(clip_vfilters(c, W, H, ratio, tonemap) + [f"fps={fr.numerator}/{fr.denominator}"])
            cmd = [FFMPEG, "-y", "-v", "error", "-nostdin", "-ss", f"{a:.4f}", "-t", f"{b - a:.4f}", "-i", c["path"],
                   "-an", "-vf", vf, "-q:v", "2", "-start_number", "1", os.path.join(d_in, "%08d.jpg")]
            self.proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True,
                                         creationflags=NO_WINDOW)
            self.proc.communicate()
            if self.cancelled:
                raise Cancelled()
            cnt = len(os.listdir(d_in))
            if cnt < 2:
                _log(f"RIFE: no frames extracted for piece {a}-{b}")
                continue
            nf = max(cnt + 1, int(round(cnt / sp)))
            self.status_cb(label + ": 0%")
            self.proc = subprocess.Popen([exe, "-i", d_in, "-o", d_out, "-n", str(nf), "-m", model, "-f", "%08d.jpg"],
                                         stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                         creationflags=NO_WINDOW, cwd=RIFE_DIR, bufsize=1)
            done_n, tail = 0, []
            for line in self.proc.stdout:
                if "done" in line:
                    done_n += 1
                    if done_n % 4 == 0 or done_n == nf:
                        frac = min(1.0, done_n / nf)
                        self.progress_cb(b0 + span * frac)
                        self.status_cb(f"{label}: {frac * 100:.0f}%")
                else:
                    tail = (tail + [line.strip()])[-8:]
            self.proc.wait()
            if self.cancelled:
                raise Cancelled()
            got = len(os.listdir(d_out))
            if self.proc.returncode != 0 or got < nf:
                _log(f"RIFE failed (code {self.proc.returncode}, {got}/{nf} frames): " + " | ".join(tail))
                self.status_cb(label + " failed - using frame blending for this section")
                shutil.rmtree(d_in, ignore_errors=True)
                continue
            shutil.rmtree(d_in, ignore_errors=True)
            D = (b - a) / sp
            self.rife_map.setdefault(i, {})[j] = (len(clips) + len(self.extras), W, H)
            self.extras.append((d_out, nf, D))
        return pbase + pspan * share, pspan * (1 - share)

    def _encode(self, out, clips, st, pbase=0.0, pspan=100.0, tag=""):
        """clips: list of dicts (path,start,end,w,h,fps,audio,bitrate).
        st: target_mb (or bitrate_mbps), res_mode, codec, audio_k, ratio, xfade, T.
        Returns the final file size in bytes."""
        n = len(clips)
        ratio = st["ratio"]
        sizes = [crop_size(c["w"], c["h"], ratio) for c in clips]
        refw, refh = max(sizes, key=lambda s: s[0] * s[1])
        fps = max(c["fps"] for c in clips)
        durs = [pieces_duration(get_pieces(c)) for c in clips]       # after speed changes
        xf, T = (st["xfade"], st["T"]) if n > 1 else (None, 0.0)
        if xf:
            T = min(T, 0.45 * min(durs))
            if T < 0.05:
                xf, T = None, 0.0
        total = sum(durs) - (n - 1) * T
        mix = bool(st.get("mix_audio"))
        n_tracks = out_tracks(clips, mix)
        any_audio = n_tracks > 0
        audio_k = st["audio_k"]
        tonemap = bool(st.get("tonemap"))
        mute_fast = bool(st.get("mute_fast", True))
        VHS["on"] = bool(st.get("vhs", False))
        AUD["fast"] = aud_key(st.get("fast_audio", FAST_AUDIO[0]))
        AUD["slow"] = aud_key(st.get("slow_audio", SLOW_AUDIO[0]))
        AUD["deep"] = st.get("slow_pitch", SLOW_PITCH[0]) == SLOW_PITCH[0]
        AUD["high"] = st.get("fast_pitch", FAST_PITCH[1]) == FAST_PITCH[0]
        AUD["fast_vol"] = int(st.get("fast_vol", 70))
        AUD["slow_vol"] = int(st.get("slow_vol", 150))
        ZOOM["motion"] = st.get("zoom_fx", ZOOM_FX[0]) == ZOOM_FX[0]
        ZOOM["len"] = float(st.get("zoom_len", 0.4))
        ZOOM["sound"] = st.get("zoom_snd", ZOOM_AUDIO[0]) == ZOOM_AUDIO[0]
        ZOOM["vol"] = int(st.get("zoom_vol", 80))
        mode = interp_key(st.get("slow_interp", SLOW_INTERP[1]))
        SLOW["blend"] = mode != "repeat"
        SLOW["rife"] = mode == "rife"
        audio_bps = int(audio_k.rstrip("k")) * 1000 * n_tracks
        fixed_mbps = st.get("bitrate_mbps")
        if fixed_mbps:                  # fixed video bitrate: one pass, no size limit
            v_bps = fixed_mbps * 1_000_000
            scale = choose_scale(st["res_mode"], refw, refh, fps, v_bps)
            W, H = even(refw * scale), even(refh * scale)
            if SLOW["rife"] and not self.rife_done:
                pbase, pspan = self._rife_prepare(clips, W, H, ratio, tonemap, pbase, pspan)
            graph, vl, al = build_graph(clips, W, H, ratio, xf, T, fps, tonemap, mute_fast, mix, self.rife_map)
            label = f"{tag}{v_bps / 1_000_000:g} Mbps, {W}x{H}"
            self.status_cb(label)
            part = f"{out}.part1.mp4"
            try:
                cmd = self._cmd(part, clips, graph, vl, al, st["codec"], v_bps, audio_k, self.use_hwdec, tonemap)
                ok, log = self._run(cmd, total, label, pbase, pspan)
                if self.cancelled:
                    raise Cancelled()
                if not ok and self.use_hwdec:                  # retry without CUDA decode
                    self.use_hwdec = False
                    cmd = self._cmd(part, clips, graph, vl, al, st["codec"], v_bps, audio_k, False, tonemap)
                    ok, log = self._run(cmd, total, label + " (no hw decode)", pbase, pspan)
                    if self.cancelled:
                        raise Cancelled()
                if not ok:
                    raise RuntimeError("ffmpeg failed:\n" + log)
                size = os.path.getsize(part)
                os.replace(part, out)
                self.progress_cb(pbase + pspan)
                return size
            finally:
                try:
                    os.remove(part)
                except OSError:
                    pass
        target_bytes = st["target_mb"] * 1024 * 1024

        v_bps = target_bytes * 0.96 * 8 / total - audio_bps
        capped_by_source = False
        rates = [c["bitrate"] for c in clips]
        if all(r > 0 for r in rates) and v_bps > max(rates):
            v_bps, capped_by_source = max(rates), True
        if v_bps < 20_000:
            raise RuntimeError("That size is too small for this length of video. "
                               "Trim it shorter or raise the size limit.")
        MAX_PASSES = 6
        parts = []                      # temp outputs of every pass
        best = None                     # (size, path) - largest pass that fits the limit
        lo = hi = None                  # bitrates that fit / did not fit
        try:
            for attempt in range(1, MAX_PASSES + 1):
                if self.cancelled:
                    raise Cancelled()
                v_bps = max(v_bps, 20_000)
                scale = choose_scale(st["res_mode"], refw, refh, fps, v_bps)
                W, H = even(refw * scale), even(refh * scale)
                if SLOW["rife"] and not self.rife_done:
                    pbase, pspan = self._rife_prepare(clips, W, H, ratio, tonemap, pbase, pspan)
                graph, vl, al = build_graph(clips, W, H, ratio, xf, T, fps, tonemap, mute_fast, mix, self.rife_map)
                label = f"{tag}pass {attempt} - {v_bps / 1000:.0f} kbps, {W}x{H}"
                self.status_cb(label)
                part = f"{out}.part{attempt}.mp4"
                parts.append(part)
                cmd = self._cmd(part, clips, graph, vl, al, st["codec"], v_bps, audio_k, self.use_hwdec, tonemap)
                ok, log = self._run(cmd, total, label, pbase, pspan)
                if self.cancelled:
                    raise Cancelled()
                if not ok and self.use_hwdec:                  # retry without CUDA decode
                    self.use_hwdec = False
                    cmd = self._cmd(part, clips, graph, vl, al, st["codec"], v_bps, audio_k, False, tonemap)
                    ok, log = self._run(cmd, total, label + " (no hw decode)", pbase, pspan)
                    if self.cancelled:
                        raise Cancelled()
                if not ok:
                    raise RuntimeError("ffmpeg failed:\n" + log)
                size = os.path.getsize(part)
                if size <= target_bytes:
                    if best is None or size > best[0]:
                        best = (size, part)
                    lo = v_bps if lo is None else max(lo, v_bps)
                    if size >= 0.90 * target_bytes or capped_by_source:
                        break
                else:
                    hi = v_bps if hi is None else min(hi, v_bps)
                # next bitrate: proportional guess, kept inside the known bracket
                guess = (v_bps + audio_bps) * (target_bytes * 0.985 / size) - audio_bps
                if best is None and attempt >= 3:
                    guess *= 0.92                              # still too big: be more aggressive
                if lo is not None and hi is not None and not (lo < guess < hi):
                    guess = (lo + hi) / 2
                v_bps = guess
                capped_by_source = False
            if best is None:
                raise RuntimeError(f"Could not get under {st['target_mb']:g} MB. "
                                   "Lower the size limit or trim the clips shorter.")
            os.replace(best[1], out)
            parts.remove(best[1])
            self.progress_cb(pbase + pspan)
            return best[0]
        finally:
            for p in parts:
                try:
                    os.remove(p)
                except OSError:
                    pass


# ------------------------------------------------------------------ tooltips
TT_COLORS = {"bg": "#2b2b2b", "fg": "#ffffff", "border": "#555555"}


ICONS = {"dark": {"settings": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAAC+UlEQVR42o2Uv28jRRTHv29m9rejpIqVCKFDCIFIiMvL0aVAoqHiwj9AB8Vdzz8BHScqhJCQ3cI1FOmQuGu4O+c6zsRIsfeXHXu96+zseB+NF4wUjnvVm6f5aN6bme8X2AhmFt1uVzbrwWDgJklynCTJ8WAwcJt6t9uVzCxwUzAzNXm/37eZWYZh2JlOpzydTjkMw87Z2Znq9/v2TYxqOiGiejQaHQBo7e3t/QoAl5eXtlIKAGCMsU9OTgwAjEaj2wAWRHTesLRukaMo6jiO8zOAYLVafQzgdcuyPlutVocAIKV8rrX+pq7r323b7gHIy7L8YHd39wkAEr1ej4iIlVKeEMITQniWZT1stVpfCyGOjDG1MaYmosOtra2vPM/7UQjhSSldpZRHRNzr9YjWo1lEVIVh+GEQBD8tl0sGUDuOYwVBAADI8xxlWVZERJ7nidls9tH+/v7DhhUAQEQVACilXrNtWwCogyCwjDHP0jS9n6bpfWPMsyAILGZmy7KEZVnvbrI0HA493/ffUEr5xpjvXNd9m5nZGHOutX6/3W4vACAMw5Zt279IKQ8BoCzLCwCfENGiKIo/hO/7HSI6B/BYCPHOcrlcBUEgtNYP2u32gpldZnbb7fZCa/0gCAIqy3IlhLhlWdYjInru+37n5k/1ikFE/+TD4dDb2dm5Vdd1YIz53nGctwD872ha6z+Z+W4z2r9OiOP4c601x3Gsi6LgyWTyNIqie1EU3ZtMJk+LouAoinRVVZwkyRebrNp8/qqqXmitayKiPM8rx3He297e/hIAiqJAnucVEZHWmquqerLJohFpmqZ35vP5Yj6fc5ZlrLXm+XzOcRxXcRxXs9mMtda8WCx4vSdL0/ROI2J1enrKzExRFC2llEtmlsx89/r6+tB13U+llG+uL/bF1dXVt0T0m1LqByHEtTFmuRYu/20fazEerAUJAEiS5DjLMs6yjJvT1/XbFxcXB5vsS21kPB4fNTYyHo+PmFn+l43gVY2NmV9qbH8BupkF3+AZg2UAAAAASUVORK5CYII=", "shortcuts": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABcklEQVR42q3Uv2qUQRQF8N/sTpLCSoW8gYhFsPIJbKz8g52iCGksbAR9A2tRLASxsrER0mljpYVPoBBNYaFYiDiI6woxWZvzybBElNULw3c+5t4zZ+6fKa01/8NqvqOsRWyGnYqC3axFrdQwHsVZLIf4l0P257EcPMMTPC+ttWN4hm18TUDFjzguYSeBtVM/zv8BnKk4j+9YzWbBNOrGwTWE0+RyJRhe4vIozpMo2sTbOD/Ft+C7wQdxPfhIiCZ9jgYlt/El0h9gI/gxXuETXuAa3iRmrCv5kMib2IeTuB+Vp0O4jUvJ50dcGUqP0hOJ3Fs4l5zcwYUOX02+7uHGXg059NAWDuF9FKzmqgOeppqH529Tu+4scdrCoxBOomY5uOIDTnUTsTsQjeLQN9tmlJS5gwrezTXlEmpprR1PqV8PpcTnqNursyv2d3gN6yXTfwIXwz7q2uG3Q9qNyAYeltba/Awt/IzM/qDgr58RkfpP9hPwKHZMRrEw+gAAAABJRU5ErkJggg==", "presets": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAACeklEQVR42q1UwYoUSRB9L7OyerqqnSmme2o9rnhYPCjiyN78gL26J/e0sOof7MU/WAYWLwur7M2rX+BFUBAR9C7evIxTk9U9PTXZOVVWZniY7j4sIgz4IMmAiAxeRLxIAoCIKJxBcD4QAEhGfC8QAA4PD2+ORqOH3vuLIQQfY2y11psikgCYAdAANkm2IQSnlMqUUmmWZZ+apnlQluULtaT2b4xxFELYA9CUZbkrIk8B7BljbgC4CGAPwNuyLHdJvosx7oUQftJaP1rTstZ+ODg4eAwAVVX9JSKn+/v7Py7ZWmvt82XcbRE5tdbeXsa+stZ+XJdW1/XrPM+vOedkMBhkeZ7DWiskPxdFkfZ9j5OTk05rnRZFgaOjI/R9f5plWbJYLN5PJpOrCQDEGNn3/VBE/vNniCQNAM5ms+6sepoQQrTW9iQTEUlCCL+LiAaAZNkj7b3vdnZ27p1nUtbaX0kakmv9gCTn8/lYRBIRMcv7a2flywColfbU99JRsjJERLa2tupzvO2ttVFEuE4kImFjY8NYax+RPI0xCgBNkiLSK6WwLCcCiAAUSW2MudC2rRURJgCglIrGmL7ruvt5niNNUzjnEELAaDRCjBGLxQLGGAyHQ3jv0bYttNZCMqx7JCLjpmmejMdjNk3zN8nu+Pj4SlEUnM1m0/l8/rIoCjrn7pDsnHN3tre36b1/Q/ICSVn1yGVZdr2qqj8A/AwgTdP0t7quPwLYFpHLdV3fjTHeApCS/KWqqnwwGFzqum6+VvZ0Or01HA7/8d7/8P+lJTldLu8mgDbG6JIkyQCkWZZ9cs79OZlMnn11HKtJnNcHEVHfDPhG0tWn+AXdr4jjw2ZKHAAAAABJRU5ErkJggg==", "effects": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABWklEQVR42q3UPUsdQRTG8d/ueoUUikmhVqkkiJjGIoVNIIUmzYUkjaQIsQppxEryFQSxlkDwG6RPbCTEJl9AMJDKIuWCNuJ9SXNW5r6yFzxw2Jk5M3/OPOfMZmVZug/LR6w3MDNkfSr8IzaT+QCoiO8GviHr29MK38OrZN6zqUAHr/EFz3Eca3lA3+FTzJdj/AyyRKMsfA7vsY23+BvxDs6whFnchO/jMOsTO0M3NFrAZWTaDs2uYt85vmO3ivdr1A3YbUAaAVnEbxzhAf7gX08GY8rfCOAiTrAa0JUAFUkBRpa/GAK5wgtcxLmqYvkoUJFcJ4U08TP6po1HeBlFyPIJIKeYjoOP8RlfsT6sj8ZBqky6eIo3mMdWKnYdSCt5Vh2s4QN2kGdlWVaBOpD+frsDV1d7gh81IWm/VUmYikEz7n1dA5LCuulvIccBHuJXTciADevsu3Qnsf/iI3I2b4Ev5AAAAABJRU5ErkJggg==", "spk": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABX0lEQVR42p3Uv0tbURQH8M9LIoi4dBRxFhQURwtaEAQHQUTcXZxcnJRCF2kXBf8AXdq5VEQRFDdHJydXOzi4RkEQTdIu58olJLzggcO7vPO93/P7FvV6XQ9SQasMUCZFGUkZURH6D2NdbO9SK3HQwiE+YTXwjSDveKESoBr6slR+Yh1PmeMqdvEnu1vknhuhbxjEOdbCltJ4QRMPWMFW2KsptVnMZSEvYzIu9kcUsIhR7OMzdnCAx0S0gK9tabeyGlYy3AaO8T3q9gWnCfAcab3Et9XW0VSzH3FewW1kMNXrHPUkKfSBrGvtUeQOv8X5COPRhJuc6Bq/ItQC8xjGa2AS6QX+4g6/oxRXOdFpaJIRnGEiSBLRWehmFHobj6h1G8h7zOAybI3ApVEYivT2wt4sumx/2vYqTmJIl7MV6VpsHWaoElO8hOn438yWVr5zRcl7VHRa0I+8R6mLpfP2H+uiWEydxe9LAAAAAElFTkSuQmCC", "spk_off": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABTklEQVR42p3Uv0okQRAG8N8MK4iYCGZiZqLBGW+gHAfKYSLCmpt4HHdPcIGRPoLC/eFQMBIxEIyMTH0OX2ARRPHW3QusXoZm1lksaIbu6frqq6qvuuh2u8awEv2mC01WNIE0ARWxBlhqAmo1BOjjN2awHfd7WTAYlBXHVqyJSirH2MV95phsEGvIqJ/VYRoXWI/zFPBfxriMwI8JaBWfEjq2sIwnTFYcz4NVJ/ZnWEA7AX3Gj4x2v6aGNzjEfgTt4Aue08WHKGIvnMtMO+l7FOn9iv1X/EE5jo7GssRoqtI1GYtqcb9HageR2s+49zc53uIkfhZYwxyeM/CP0c292C/iG06LEbM2jyt8iIin2KlhOGz/KEHeYQXX8S/pZyLTXQ+PbwmyDDVv4BKzFSWrG5FRs5bU/IJNtOP8pWZEXhEb3qOihsW73qPUxUa9/Qcw5FHCGwU/egAAAABJRU5ErkJggg==", "play": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABHklEQVR42p3UPS8FQRTG8d/ujuSqJKJF4hsoJSJRipaI1jdQq/Ro1EqJQq2n06CRiC+gvRGN5O6lOZMs7suuSTY7Oyfz7P85Z84U/X7fr1HiK57WoxyxNgyR6r9CRbxXsYA64mVXoUywjwfsBd0wYkVXa+9YxBVugrAOu6mLUBUUn9jCPc4wj0GQlW2E8noVJDM4xCMOguyP3WmJrGLjAEu4wC3WGnZbCeVqpthUYwN3uMRKxItSt5EJEpYxm3/WVqhukL1iB+t4zjlLUwSGjVx94ASnMf/RSmmChQF68X2No6DRqOjE8mcbPTxhG7shkiJW/940imgu0I9xHgezeQxGl7ZxjZSRk0284WWcjWlC41ql1b2UJvRf3eWAfQPNzkqd1OHglgAAAABJRU5ErkJggg==", "pause": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAAAwUlEQVR42u2UQQoCMQxFX8ZZKKKDOBfQA3oPL+fehbh0WwbcqGNnEyHU1Kq4NFDa/N+m+QmthBBITBI/FnAAap4t4lsOdwMJ0JjbL8BZ1xNgbPZ2QP9wqiTtBtgBBx1bc3Cj2F7nlY3hZdQCM/XnhpsCi5yaypF71XpE4GbwXjHLvyy2GKni4OJ00M3oK/sHKlv94XOIOb7U/hz3VvtjQYXLS/KNjIC1uaADTrpugaXZe9RH7Qb6abGrREbM1O5uDw08lSwkO3yBOwAAAABJRU5ErkJggg=="}, "light": {"settings": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAACnElEQVR42q1UvYpbRxg9Z2Z00fXqEUTWRlz27h8KqNjYjaq4CqSIZT+Au6RxmSKQKr0DbkxIFVKpT+NKjUUCCxHL7moRa7LkFRTuIs18J0VGRusqhU81zMz55pzvZ4i7cAAIIAHA7u5uuyzLTwGgaZo/b25ubvM9D0AAbJu4AfNBAlCMRiNfFMUeySnJaVEUe8PhMAAo8h3LnDuBHAD1er3DqqpOAKzG43EiWbx/hSwmk0kEsKqq6qTX6x1mVW6jwgFQVVX9EMIbSTskvzKzT5xzX0s6yoEuzOwnktcAxiT/iTF+vlgsZgAYNpa89yWAkmQJ4DfvPSRBUsyBjrz3P0oCAEhKmSMAzgFIg8GgNZ/PpymlJwBMUkoprQEgZABASmltZhGApZSezefz6WAwaAFIDgBOT0/XAOCc65J0ksx735J0FmN8EWN8IenMe98CIPcfDra57Ha7ZbvdfuC9v0fyF5J7We45yUcXFxdLADg4OOhIegvgKFu7MbOnkpZN0/wVOp1OH8CUJCTBzGIIIaSUXl9eXi6Hw2EbACaTybKu69fe+1cxxkjyvvf+D0nodDoPHT4S2O12y7Is7zvndgD86pyr/qe1v83sycYat6PWdf2Nc+5VSmntvW+Z2Zmkn3P5nzvnjlNK6xBCK8b43dXV1Q93OjuXEGb2TpIBYEppTfI4hPAyhPCS5HFuCUoSydk2l3kAU13XD0m+AbCTFbxPfm6NsNnL1pYAHs/n8ykA73I+mFJqADSSbiV9YWbfSlqQdCQdgHdm9r2ZfSmpIXmbOQQgblm0Xq936JzrLBaL33POPiM5zQoe5dexv79/slqtltfX1+cbLj/4RpTXxWg0SrPZ7NB7P8vj0e/3++fj8dgDWH3I4cf62P4FFVBzhIyN1BAAAAAASUVORK5CYII=", "shortcuts": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABb0lEQVR42q3UvWtUQRQF8N/sTpLCUkhpJz6LYJX/wcoP7BRFsAmYJqCFvbUoFoJY2dgI6bSx0sLSSiGYwkKxCCKI6woxWZvzZHhElNULwzuPuffMmfsxpes6/8NqvqOseWyGvYqC/ax5rdQwnsA5LIb4l0P2h1gOnuEpXlSs4jl28TUBFT/iuIC9BNZG/Tj/N3C24gK+YzmbBdOoGwfXEE6Ty6VgeI21UZwnUbSFd3F+hm/B94IP43rw8RBN2hz1Su7gS6Q/xGbwE7zBJ7zENbxNzFhT8j6Rt3AIp/AgKs+EcBeXk88drPelR2mJRO5tnE9O7uJigzeSr/u4eVBD9j20jaP4EAXLuWqPp6nmseFtatOdJU7beBzCSdQsBld8xOlmIvZ7olEc2mbbipIyOKjg/aApF1BrEnk1wZM4f466gzr7CF4FV6zgSsn0n8SlsI+advjtkDYjsolHpeu64QzN/YzM/qDgr58RkfpP9hPQqmsDGzQQ3wAAAABJRU5ErkJggg==", "presets": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAACK0lEQVR42q1UPWtUURScOfe+gHETIdlk04rFvi20SGytLC0VJJ0I+g9s/AsBEUTiip2t5AdYmkYEbd976x+wcBPUF5Ps7j1jkbdhCzEGcprDZeZ+nDlnLnES1mThfMEmOy4qCADdbvemmT2XtCbpkOSxpEWSUdI+yQBgEcAxgAMA8wDmSH4D8LQoig8GACRfAWhJ2gLwK8a4AeCdu2+Z2bqkNXffAvA5y7INAF8abldS/1QbSVfc/VNVVX0Au83NrweDwba7D0kWg8FgG8BOg+1UVdV396+SLs+W9jGEcMPdRXLezJBSkqRxjHFOElJKIzObMzO4OyQdkYzuXlVVdT1OtZJ0SdIbAIfj8dhJZiSZUhqdVM/M3T2lNCEZSUYADwAEAIiNRsHdR1VVPTpPp/I8vwsgA6A428E8z5c7nc6Puq7ZarX+OlNTrK7rubqubTp7dlFzNPsilWU5LMvyf/dO8jx3STw9SFIysyzP8z6Ao6Z7oRF50nAiSXd3NzNr8AVJ3wEwznhlQvIxSYQQ4H5iHzODJEjCFJtZC0Ca1Wg5pfS2KAqmlJ6RHKWUekVRcDKZ7KWUdouioLtvmtnI3Tcb7icACwAUAKDdbj8MIXSWlpaSmd0xs6vuvr+ysnKN5H2SWbvd/inpdghhPaX0e3V1dZnkPUkHw+HwBQGg1+vdIvnS3TuSDhsbTE271wzfIoBjSQckT00r6UlZlu/P+mfOi8HOIvzjUAOAPxcGO5N1f0WFAAAAAElFTkSuQmCC", "effects": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABWElEQVR42q3UsWoUURTG8d/c2RVSWBgXjSA2gixBX0C0sYjaBEwlvoCNiJX4EPba5BW0VhsRQbCwFUypgnZCtDLZtflGrpNdnYAHDjP3nnv+8517DtNMp1P/w8qS/TGOLtgfxW/jarU+AGrz3MATNL0ze/H7uF6ttZPJpIbMcAOPsI6zeFrBbuFSvInqgs81SIJf8QOncQffEpvjMbZwHKdwGR/xpulddpOEMU7iU5Tu5+u7Ofcez3Cvi/fvaB7Yz0DGgazhbRStYAdf/lDwl/aPA1zDC5wPdD2gtmrA0va3CyC7uIIPyes6VpaB2qqcGrKJV5mbfaziWjrdlENAXuJIEs/gAbZxsa/oX5BOyRwXMgYncLMeyCGQvUBK7ul19u6ijBIYAulslhF5Fy+YdaWdw/MBkP68lYCN8rKZur8PgNSwef1bKHiIY6l7COSALZrs33IPY78ANiJkbyJ9mVAAAAAASUVORK5CYII=", "spk": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABZ0lEQVR42pXUvUtbYRQG8N9NUpDi0lFKZ0MLSscWakEodBAkhO5dOnVxUgSX0i4V+gfoonNRSkNAcRNcnDq56tChcwqFoPnocl55CYk3Hjjcyz3Pec73Ler1uimkgkEZoEyKMpIyoiJ0iKcTbLdSKwkwwC4e4V3ge0E+1qESoBoeZKXs4QP+ZoGr+IqDzLfII/dCbzCLI7wPWyqjiz7+oIn1sFdTaUtYzlJuYDEcZyILWME8vuElPmEHnUT0FpsjZQ+yHlYy3Ef8wOfo22u0EuBflNWN52BkoqlnX+K9iYuo4Pm0ezSVpNQfZlMbzSIPuBXvh3gWQ/iVE51jP1It8AaPcR2YRHqMK1zie7TiNCdqhSZ5gjYWgiQRtUPXotEb6KA2aSF/4xVOwtYLXFqFuShvO+z9YsL1p2uv4mcsaSM7kYnNNmaHKrHFq3gR3/vZ0cpvrnbHRNNp9HE24ji87/8oTbF03/4DLXpUjZyIzfYAAAAASUVORK5CYII=", "spk_off": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABUElEQVR42o3Uv0ocURQG8N8MLkiwCdgFO4uNhdYWigQiYhME09sook+QIlV8hAQ0IUTYSsRCsLJKYZPnyBsEwspmd9bmXBkuM5k9cBnOvefPd875zhT9ft8MUqLqMuiSoitIV6AizhQrXYHmOhJU+IqXeB/24ywZTMua41ycXq2UHzjEn8wxyTTOM6Iq68MCbrAd9ynhvwxxGYmHKdAm3qTo2MMaHjFfc7wOVPuhX2EZ6ynQDj5ksKuGHv7EZ3yKpPs4wigZ/o0mjsO5zLiTvl+ivIvQj/EN5Sw8mkkSohe1qclQ1Jt7GqWdRWnnYfc9Of7CZTwWeItXGGXBt2KaH0N/jRMMipZdW8IdViPjAAcNCJ/H30bI39jAfbwl/vQy3o0x/B8hy2DzLm6xWGOyphVp27XE5gneYT3uJw0r0rn9VWSc4KEF0cz/ozTFTr49AcGVTQIavQmRAAAAAElFTkSuQmCC", "play": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAABGklEQVR42p3TMUvEQBCG4SfJCmcliK0KVtdaCmIvtorY+g+srezVxtpSsLC+VrSzURtB/BliIySnzQTiGe8SB0I2O+yX95uZzYbDoYnI8RVP58hb9sYhUvxXKIv3OpZQRT7vK1QTHOAR+0E3jlzW19o7lnGNURBWYTf1ESqC4hPbeMA5FlEGWd5FqN4vgmQOR3jCYZD9sjurkEUcLLGCS9xho2G3k1DdzRSHKmzhHldYi3yW6xc1QcIq5uufdRWqGmRv2MUmXuqapRkC40atPnCKs1j/uEppioUSg/i+wXHQaHR0avtrGwM8Ywd7IZIiV00eaiNaCPQTXMRgNsegNVJLPUYxL69/2egqdDtxVaoubU1T7l/VZ8C+Ad3tRI/BykyDAAAAAElFTkSuQmCC", "pause": "iVBORw0KGgoAAAANSUhEUgAAABIAAAASCAYAAABWzo5XAAAAyUlEQVR42u2UMQ7CMAxFn0sHEIIK0QvA1NNxDy7HjhBiZGGqxAK0YTGSFRxSmLEUJf4/cfxtJdI0DZFJ5IcMDkDJuwV8S+FuIAEqc/sNuOp6AozN3hboXk4RpV0BO+CoY2sObhTb67yyMbyMamCm/txwU2CRUlM4cu9ajwA8DN4pZvmPxRYjVRxcnA66Gf1k/0B5K798DiHF59qf4ga1P2RUuLxE38gIWJsLWuCs6xpYmr0nfdSutA44JDK56Bhc7CKSERK16+2hJ02FKFe4/U9BAAAAAElFTkSuQmCC"}}


def _hex(c):
    return None if c is None else tuple(int(c[i:i + 2], 16) for i in (1, 3, 5))


def corner_image(master, k, R, outside, ring, inside, ringw=2):
    """Anti-aliased quarter-circle corner (k = nw/ne/sw/se) as a PhotoImage with real transparency.
    outside / ring / inside are '#rrggbb' or None (transparent)."""
    import zlib, struct
    cx = R if k in ("nw", "sw") else 0
    cy = R if k in ("nw", "ne") else 0
    cols = (_hex(outside), _hex(ring), _hex(inside))
    N = 4
    raw = bytearray()
    for y in range(R):
        raw.append(0)
        for x in range(R):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(N):
                for sx in range(N):
                    d = math.hypot(x + (sx + .5) / N - cx, y + (sy + .5) / N - cy)
                    c = cols[0] if d > R else cols[1] if (d > R - ringw and cols[1] is not None) else cols[2]
                    if c is not None:
                        acc[0] += c[0]; acc[1] += c[1]; acc[2] += c[2]; acc[3] += 1
            n = N * N
            if acc[3] == 0:
                raw += bytes((0, 0, 0, 0))
            else:
                raw += bytes((round(acc[0] / acc[3]), round(acc[1] / acc[3]), round(acc[2] / acc[3]),
                              round(255 * acc[3] / n)))
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", R, R, 8, 6, 0, 0, 0)) +
           chunk(b"IDAT", zlib.compress(bytes(raw))) + chunk(b"IEND", b""))
    return tk.PhotoImage(master=master, data=base64.b64encode(png))


def rounded_pts(x0, y0, x1, y1, r):
    """Points for a smooth polygon that draws a rounded rectangle (works with dashed outlines)."""
    r = max(1, min(r, (x1 - x0) / 2, (y1 - y0) / 2))
    return [x0 + r, y0, x1 - r, y0, x1, y0, x1, y0 + r, x1, y1 - r, x1, y1, x1 - r, y1,
            x0 + r, y1, x0, y1, x0, y1 - r, x0, y0 + r, x0, y0]


def draw_round_box(c, x0, y0, x1, y1, r, **kw):
    return c.create_polygon(rounded_pts(x0, y0, x1, y1, r), smooth=True, **kw)


class PopupMenu(tk.Toplevel):
    """Borderless drop-down menu with rounded corners and roomy rows (replaces the plain Windows menu)."""
    ROW, PADY, WIDTH, R = 34, 8, 270, 10

    def __init__(self, app, items):
        super().__init__(app)
        self.app, self.items, self.hover = app, items, None
        pal = app.pal
        self.key = "#010203"
        self.overrideredirect(True)
        try:
            self.attributes("-topmost", True)
        except Exception:
            pass
        self.transparent = False
        try:
            self.attributes("-transparentcolor", self.key)
            self.transparent = True
        except Exception:
            pass
        self.H = self.PADY * 2 + self.ROW * len(items)
        self.c = tk.Canvas(self, width=self.WIDTH, height=self.H, highlightthickness=0, bd=0,
                           bg=self.key if self.transparent else pal["bg"])
        self.c.pack()
        self.c.bind("<Motion>", self._motion)
        self.c.bind("<Leave>", lambda e: self._set_hover(None))
        self.c.bind("<Button-1>", self._click)
        self.bind("<Escape>", lambda e: self.close())
        self.bind("<FocusOut>", lambda e: self.after(80, self._check_focus))
        self._draw()

    def _draw(self):
        c, pal = self.c, self.app.pal
        c.delete("all")
        draw_round_box(c, 1, 1, self.WIDTH - 1, self.H - 1, self.R, fill=pal["card"], outline=pal["border"], width=1)
        for i, (label, accel, _cmd) in enumerate(self.items):
            y0 = self.PADY + i * self.ROW
            if i == self.hover:
                draw_round_box(c, 6, y0 + 1, self.WIDTH - 6, y0 + self.ROW - 1, 7, fill=pal["border"], outline="")
            c.create_text(18, y0 + self.ROW / 2, text=label, anchor="w", fill=pal["fg"], font=("Segoe UI", 10))
            if accel:
                c.create_text(self.WIDTH - 18, y0 + self.ROW / 2, text=accel, anchor="e", fill=pal["muted"],
                              font=("Segoe UI", 9))

    def _index(self, y):
        i = int((y - self.PADY) // self.ROW)
        return i if 0 <= i < len(self.items) else None

    def _motion(self, e):
        self._set_hover(self._index(e.y))

    def _set_hover(self, i):
        if i != self.hover:
            self.hover = i
            self._draw()

    def _click(self, e):
        i = self._index(e.y)
        if i is not None:
            cmd = self.items[i][2]
            self.close()
            self.app.after(10, cmd)

    def _check_focus(self):
        try:
            if self.focus_displayof() is None:
                self.close()
        except Exception:
            pass

    def close(self):
        self.app._menu_closed = time.time()
        self.app._popup = None
        try:
            self.destroy()
        except Exception:
            pass

    def show(self, x, y):
        self.geometry(f"{self.WIDTH}x{self.H}+{x}+{y}")
        self.update_idletasks()
        try:
            self.focus_force()
        except Exception:
            pass


class Tooltip:
    """Small hint window that appears when the mouse rests on a widget."""

    def __init__(self, widget, text):
        self.widget, self.text, self.job, self.tw = widget, text, None, None
        widget.bind("<Enter>", self._enter, add="+")
        widget.bind("<Leave>", self._hide, add="+")
        widget.bind("<ButtonPress>", self._hide, add="+")

    def _enter(self, e=None):
        self._hide()
        self.job = self.widget.after(550, self._show)

    def _show(self):
        self.job = None
        try:
            tw = tk.Toplevel(self.widget)
            tw.wm_overrideredirect(True)
            tk.Label(tw, text=self.text, bg=TT_COLORS["bg"], fg=TT_COLORS["fg"], justify="left",
                     padx=8, pady=4, font=("Segoe UI", 9), highlightthickness=1,
                     highlightbackground=TT_COLORS["border"]).pack()
            x = self.widget.winfo_rootx() + 10
            y = self.widget.winfo_rooty() + self.widget.winfo_height() + 6
            tw.wm_geometry(f"+{x}+{y}")
            self.tw = tw
        except Exception:
            self.tw = None

    def _hide(self, e=None):
        if self.job:
            try:
                self.widget.after_cancel(self.job)
            except Exception:
                pass
            self.job = None
        if self.tw is not None:
            try:
                self.tw.destroy()
            except Exception:
                pass
            self.tw = None


def tip(widget, text):
    Tooltip(widget, text)
    return widget


class StripedBar(tk.Canvas):
    """Plain rounded progress bar (works like ttk.Progressbar: bar["value"] = pct); turns green when done."""
    BH, PER, RAD = 26, 24, 5

    def __init__(self, parent, app):
        super().__init__(parent, height=self.BH, highlightthickness=0, bd=0)
        self.app, self._v, self._off, self._job, self.done = app, 0.0, 0.0, None, False
        self.text, self.failed = None, False
        self.bind("<Configure>", lambda e: self._draw())
        self.recolor()

    def __setitem__(self, key, val):
        if key == "value":
            self._set(val)
        else:
            tk.Canvas.__setitem__(self, key, val)

    def config(self, *a, **k):
        if "value" in k:
            self._set(k.pop("value"))
        if a or k:
            tk.Canvas.configure(self, *a, **k)

    configure = config

    def _set(self, v):
        self._v = max(0.0, min(100.0, float(v)))
        self._draw()

    def recolor(self):
        try:
            tk.Canvas.configure(self, bg=self.app.pal["foot"])
        except Exception:
            pass
        self._draw()

    @staticmethod
    def _mix(c1, c2, t):
        a = [int(c1[i:i + 2], 16) for i in (1, 3, 5)]
        b = [int(c2[i:i + 2], 16) for i in (1, 3, 5)]
        return "#%02x%02x%02x" % tuple(int(x + (y - x) * t) for x, y in zip(a, b))

    @staticmethod
    def _clip(poly, xl, xr):
        for edge, keep in ((xl, lambda x: x >= xl), (xr, lambda x: x <= xr)):
            out = []
            for k in range(len(poly)):
                p, q = poly[k], poly[(k + 1) % len(poly)]
                if keep(p[0]):
                    out.append(p)
                if keep(p[0]) != keep(q[0]):
                    t = (edge - p[0]) / (q[0] - p[0])
                    out.append((edge, p[1] + t * (q[1] - p[1])))
            poly = out
            if not poly:
                break
        return poly

    def set_done(self, flag, text=None):
        self.done, self.failed, self.text = bool(flag), False, text
        self._draw()

    def set_failed(self, text):
        self.done, self.failed, self.text = False, True, text
        self._v = 100.0
        self._draw()

    def _rr(self, x0, y0, x1, y1, **kw):
        r = min(self.RAD, (y1 - y0) / 2)
        pts = [x0 + r, y0, x1 - r, y0, x1, y0, x1, y0 + r, x1, y1 - r, x1, y1, x1 - r, y1, x0 + r, y1,
               x0, y1, x0, y1 - r, x0, y0 + r, x0, y0]
        self.create_polygon(pts, smooth=True, **kw)

    def _draw(self):
        try:
            self.delete("all")
            pal = self.app.pal
            W, H = max(self.winfo_width(), 40), max(self.winfo_height(), 16)
            self._rr(1, 1, W - 1, H - 1, fill=pal["card"], outline=pal["border"])
            if self._v <= 0:
                return
            fw = max(2 * self.RAD, (W - 2) * self._v / 100.0)
            x1 = 1 + fw
            main = ("#22c55e" if self.app.theme == "dark" else "#16a34a") if self.done else pal["accent"]
            if self.failed:
                main = "#e5484d"
            self._rr(2, 2, x1, H - 2, fill=main, outline="")
            txt, cx, cy = self.text or f"{self._v:.0f}%", W / 2, H / 2
            font = ("Segoe UI", 9, "bold")
            for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                self.create_text(cx + dx, cy + dy, text=txt, fill="#000000", font=font)
            self.create_text(cx, cy, text=txt, fill="#ffffff", font=font)
        except tk.TclError:
            pass


# --------------------------------------------------------------- clip row
X_ICONS = {"#ffffff": "iVBORw0KGgoAAAANSUhEUgAAAA4AAAAOCAYAAAAfSC3RAAABDklEQVR42n2STUpDQRCEv3mJoguRiGBciRKFxAvoRnIx75FzeA034s/Cha6MEVQEFQSRfC7sl7RPsGCYoqdqpqdmikqgAAIVMOU36rUZqsQFOmGqGhqBlaaxAhaAY+AM6Ie5DbSCd4FTYAQsAxVqUdfVR39wpw5UYmyqF7H2pe6qBbUKwSBMqmO1p3bUy6i9qkehrepds/k+hLfqefB3dRialgqppXbMe+q1c0zUw4bmT6oAT8BHqn8CDzTRaLWrXsVJz6ntSQps1mpt2kjpvakH6k4yj9V+Dqeoa+pNSm+Y7r6f0n5Rt/NzLKmjRuTtFMYg3vlEXc0n1rv38j0afEtdDF5K+uT5G07/qRXAb4rMvihNxeCeAAAAAElFTkSuQmCC", "#ef4444": "iVBORw0KGgoAAAANSUhEUgAAAA4AAAAOCAYAAAAfSC3RAAABKklEQVR42m3Suy7EURDH8c//bxUKERrrEiKRiFu8gITI0rg8hdobeA/PgkJIRKLRUCAShVBsKNYl2dhdzZzNIaaazJmZM/P9TfFWqwkr0EGJtt+W3rpWZn4Hg1FU/snpoP9vYYle7OMKs1FcQU/4VVziAH0oU7cB7GECR5jDN1oYidgMdjGOThnz17GCJ4xF4nSMfoQFNLCGOxSVbKcbbOAYozjEO+bxgR2cxvitBCHtdINV3GIKi3jBOk4ip/UfVTH2VxZvRrH/5CijUxVnWMIrngPYeQasJ5ejjeGM6Ds2sRzF1XibjQ+6cgxF10RvGxd4iP2eAth57N6V4zM6NrCVgajgOmjXY41XFMVbrZbf4TTuE/KIJX8yxm4mHfPjvc9AJWtF7DE/+B9gEFV9AwoOQgAAAABJRU5ErkJggg==", "#b3162a": "iVBORw0KGgoAAAANSUhEUgAAAA4AAAAOCAYAAAAfSC3RAAABK0lEQVR42m3SvS6EURDG8d/7WoVChIL1ESLZRCziBiREQuPjKtTuwH24j+2sQkhEotFQIBKFUIjGV7KxVjNnc4ipJnNm5sz8nykaw3VhBToo8e23pbeulZnfwWAUlX9yOuj/W1iiF3u4wGwUV9ATfhXn2EcfytRtALuYRBN1fKGN0YjNYAcT6JQx/zOW8YDxSKzF6E3M4xWruEFRyXa6wjoOMYYDvGEO79jGcYzfThDSTldYwTWmsYAnrOEoctr/URVjf2bxVhT7T44yOlVxgkW84DGAnWbAenI5vjGSEX3DBpaiuBpvs/FBV46h6JrobeEMd7HfQwA7jd27cnxEx1dsZiAquAzaz7HGC4qiMVzP77CG24Q8YsmfirFbScf8eG8zUMnaEbvPD/4Hi35UdWOp0ogAAAAASUVORK5CYII=", "#ffc2c2": "iVBORw0KGgoAAAANSUhEUgAAAA4AAAAOCAYAAAAfSC3RAAABKElEQVR42m3SzyqFYRDH8c/7OhYWEhvHn0idkn+5AUWKjT9XYe0O3IdrwUKvkrKxYYGUhVjIxkHJ8drMc3rIrKZ5ZuaZ+f6mqKtKWIEaJb79tvTWtTLzawxGUfknp0b/38ISvdjDBWaiuIGe8Js4xz76UKZuA9jFBA4xiy90MBKxaexgHHUZ8z9jGQ8Yi8RWjH6IebxiFTcoGtlOV1jHEUZxgDbm8IZtVDF+J0FIO11hBdeYwgKesIbjyOn8R1WM/ZHFP6PYf3KU0amJEyziBY8B7DQD1pPL8Y3hjGgbG1iK4ma8zcQHXTmGomuit4Uz3MV+DwHsNHbvyvEeHV+xmYFo4DJoP8caLyiKuqryO2zhNiGPWPInY+zPpGN+vLcZqGSdiN3nB/8DFgBXlVGcQK0AAAAASUVORK5CYII="}    # remove-clip cross in the colours FlatButton uses


class FlatButton(tk.Label):
    """Small borderless button for the clip headers (the theme's rounded buttons show their corners on the
    card colour).  kind = 'normal' or 'danger'."""

    def __init__(self, parent, app, text, command, width=3, kind="normal", size=9, pady=4):
        super().__init__(parent, text=text, width=width, font=("Segoe UI", size, "bold" if kind == "danger" else "normal"),
                         padx=10, pady=pady, cursor="hand2", bd=0)
        self.app, self.command, self.kind, self.hover = app, command, kind, False
        self.on_accent = False
        self.icon_x = text == "✕"                          # drawn as an image (same look on every PC)
        if self.icon_x:
            self.config(text="", width=0, padx=12, pady=7)
        self.bind("<Enter>", lambda e: self._set_hover(True))
        self.bind("<Leave>", lambda e: self._set_hover(False))
        self.bind("<ButtonRelease-1>", self._click)
        self.recolor()

    def _click(self, e):
        if 0 <= e.x < self.winfo_width() and 0 <= e.y < self.winfo_height():
            self.command()

    def _set_hover(self, v):
        self.hover = v
        self.recolor()

    def recolor(self):
        pal = self.app.pal
        dark = self.app.theme == "dark"
        if self.kind == "solid":
            self.config(bg="#dc2626" if self.hover else "#ef4444", fg="#ffffff")
            return
        if self.on_accent:                                  # on the blue title bar of the active clip
            base = pal["accent"]
            txt = "#06202f" if dark else "#ffffff"
            if self.kind == "danger":
                bg, fg = ("#ef4444", "#ffffff") if self.hover else (base, "#b3162a" if dark else "#ffc2c2")
            else:
                bg, fg = ("#8fdcff" if dark else "#2a84d6", txt) if self.hover else (base, txt)
        elif self.kind == "solid":                          # always red (Clear all)
            bg, fg = ("#dc2626" if self.hover else "#ef4444"), "#ffffff"
        elif self.kind == "danger":
            bg, fg = ("#ef4444", "#ffffff") if self.hover else (pal["card"], "#ef4444")
        else:
            bg, fg = (pal["border"], pal["fg"]) if self.hover else (pal["card"], pal["fg"])
        self.config(bg=bg, fg=fg)
        if getattr(self, "icon_x", False):
            cache = self.app.__dict__.setdefault("_x_icons", {})
            key = fg if fg in X_ICONS else "#ef4444"
            if key not in cache:
                cache[key] = tk.PhotoImage(master=self.app, data=X_ICONS[key])
            self.config(image=cache[key])


class RoundButton(tk.Canvas):
    """Rounded button in a fixed colour (the theme's own buttons cannot be recoloured). Same size as a normal button."""

    def __init__(self, parent, app, text, command, color="#ef4444", hover="#dc2626"):
        self.app, self.text, self.command, self.color, self.hover_color, self.hover = app, text, command, color, hover, False
        try:
            probe = ttk.Button(parent, text=text)
            probe.update_idletasks()
            w, h = max(probe.winfo_reqwidth(), 60), max(probe.winfo_reqheight(), 24)
            probe.destroy()
        except Exception:
            w, h = 80, 30
        super().__init__(parent, width=w, height=h, highlightthickness=0, bd=0, cursor="hand2")
        self.bind("<Enter>", lambda e: self._set(True))
        self.bind("<Leave>", lambda e: self._set(False))
        self.bind("<ButtonRelease-1>", self._click)
        self.bind("<Configure>", lambda e: self.recolor())
        self.recolor()

    def _set(self, v):
        self.hover = v
        self.recolor()

    def _click(self, e):
        if 0 <= e.x < self.winfo_width() and 0 <= e.y < self.winfo_height():
            self.command()

    def recolor(self):
        try:
            tk.Canvas.configure(self, bg=self.app.pal["bg"])
            self.delete("all")
            W, H, r = max(self.winfo_width(), 20), max(self.winfo_height(), 20), 5
            pts = [r, 1, W - r, 1, W - 1, 1, W - 1, r, W - 1, H - r, W - 1, H - 1, W - r, H - 1, r, H - 1,
                   1, H - 1, 1, H - r, 1, r, 1, 1]
            self.create_polygon(pts, smooth=True, fill=self.hover_color if self.hover else self.color, outline="")
            self.create_text(W / 2, H / 2, text=self.text, fill="#ffffff", font=("Segoe UI", 9))
        except Exception:
            pass


class ChoiceDialog(tk.Toplevel):
    """Small modal question with your own button labels. result = index of the pressed button (None = closed)."""

    def __init__(self, app, title, text, buttons, default=0, cancel=None):
        super().__init__(app)
        self.result, self._cancel, self._default = None, cancel, default
        self.title(title)
        self.transient(app)
        self.resizable(False, False)
        try:
            self.configure(bg=app.pal["bg"])
        except Exception:
            pass
        box = ttk.Frame(self, padding=(22, 18))
        box.pack(fill="both", expand=True)
        ttk.Label(box, text=text, justify="left").pack(anchor="w", pady=(0, 18))
        row = ttk.Frame(box)
        row.pack(anchor="e")
        self.btns = []
        for i, label in enumerate(buttons):
            kw = {"style": app.sty("Accent.TButton")} if i == default and app.sty("Accent.TButton") else {}
            b = ttk.Button(row, text=label, command=lambda i=i: self._pick(i), **kw)
            b.pack(side="left", padx=(8 if i else 0, 0))
            self.btns.append(b)
        self.bind("<Escape>", lambda e: self._pick(self._cancel))
        self.bind("<Return>", lambda e: self._pick(self._default))
        self.protocol("WM_DELETE_WINDOW", lambda: self._pick(self._cancel))
        try:
            self.update_idletasks()
            x = app.winfo_rootx() + (app.winfo_width() - self.winfo_reqwidth()) // 2
            y = app.winfo_rooty() + (app.winfo_height() - self.winfo_reqheight()) // 3
            self.geometry(f"+{max(0, x)}+{max(0, y)}")
            self.grab_set()
            self.btns[default].focus_set()
        except Exception:
            pass

    def _pick(self, i):
        self.result = i
        try:
            self.destroy()
        except Exception:
            pass


class ClipRow(tk.Frame):
    """One clip with its own thumbnail timeline and trim handles."""

    CR = 9                                               # corner radius of the card

    def __init__(self, parent, app, path, info):
        super().__init__(parent, bd=0, highlightthickness=2, bg=app.pal["bg"],
                         highlightbackground=app.pal["border"], highlightcolor=app.pal["border"])
        self.app, self.path, self.info = app, path, info
        self.start, self.end, self.playhead = 0.0, info["dur"], 0.0
        self.drag = None
        self.token = 0
        self.index = 0
        self.active = False
        self.selected = False
        self.tl_h, self.lane_h = COMPACT_TL_H, COMPACT_LANE_H
        self.sections = []          # speed sections: {"a","b","speed","var","entry"}
        self.zooms = []             # zoom sections: {"a","b","x","y","w"} (x, y, w = the area, fractions of the picture)
        self.zsel = None
        self.sel = None             # selected speed section
        self.view_a, self.view_len = 0.0, max(info["dur"], 0.1)     # visible part of the timeline (zoom)
        self.cache = {}             # thumbnails: (level, k, n1) -> PhotoImage
        self.reset_id = 0
        self.n1, self.tw = 0, 80
        self._thumb_job = None
        self._pending = 0

        head = self.head = ttk.Frame(self, padding=(14, 5), style="RowHead.TFrame")
        head.pack(fill="x")
        body = self.body = ttk.Frame(self, padding=0, style="RowTL.TFrame")
        body.pack(fill="both", expand=True)
        self.corners = []
        for relx, rely, anc, k in ((0, 0, "nw", "nw"), (1, 0, "ne", "ne"), (0, 1, "sw", "sw"), (1, 1, "se", "se")):
            cv = tk.Canvas(self, width=self.CR, height=self.CR, highlightthickness=0, bd=0, bg=app.pal["card"])
            cv.place(relx=relx, rely=rely, anchor=anc, x=(-2 if relx == 0 else 2), y=(-2 if rely == 0 else 2))
            self.corners.append((cv, k))
        self.title_lbl = ttk.Label(head, text="", font=("Segoe UI", 8, "bold"), style="RowHead.TLabel")
        self.title_lbl.pack(side="left")
        self.hbtns = []
        btn = FlatButton(head, app, "✕", lambda: app.remove_row(self), 2, "danger")
        tip(btn, "Remove this clip from the list").pack(side="right")
        self.hbtns.append(btn)
        self.zoom_lbl = ttk.Label(head, text="", font=("Segoe UI", 8), style="RowHeadM.TLabel")
        self.zoom_lbl.pack(side="right", padx=8)
        fit = FlatButton(head, app, "Reset zoom", self.fit, 10)
        tip(fit, "Show the whole clip again (zoom: Ctrl + mouse wheel)").pack(side="right", padx=(0, 2))
        self.hbtns.append(fit)
        self.preset = None                                  # name of the bitrate preset (None = Auto)
        self.preset_var = tk.StringVar(value="Auto bitrate")
        self.preset_cb = ttk.Combobox(head, textvariable=self.preset_var, width=24, state="readonly",
                                      values=app.preset_labels())
        self.preset_cb.pack(side="right", padx=(0, 10))
        self.preset_cb.bind("<<ComboboxSelected>>", self._preset_picked)
        self.preset_cb.bind("<MouseWheel>", lambda e: "break")
        tip(self.preset_cb, "Bitrate of this clip when the clips are exported as separate files: "
                            "Auto bitrate = the size / bitrate from the Export panel. Presets are made in the Bitrate presets window.")
        self._hdr_drag = False
        for w in (head, self.title_lbl, self.zoom_lbl):       # click = select the clip, drag = change its order
            w.bind("<Button-1>", self._hdr_down)
            w.bind("<B1-Motion>", self._hdr_move)
            w.bind("<ButtonRelease-1>", self._hdr_up)
        tip(self.title_lbl, "Click to select - drag up or down to change the order of the clips")

        self.canvas = tk.Canvas(body, height=COMPACT_TL_H, bg="#202020", highlightthickness=0)
        self.canvas.pack(fill="x")
        self.tlb = COMPACT_TL_H                            # bottom of the drawing area (above the times text)
        self.tvars, self.tents = {}, {}                    # time boxes next to the trim handles
        for which, colr in (("start", "#22c55e"), ("end", "#ef4444")):
            v = tk.StringVar()
            en = tk.Entry(self.canvas, textvariable=v, width=8, justify="center", bg=colr, fg="#ffffff",
                          insertbackground="#ffffff", disabledforeground="#ffffff", relief="flat",
                          borderwidth=0, highlightthickness=1, highlightbackground=colr, highlightcolor="#ffffff",
                          font=("Segoe UI", 8, "bold"))
            en.bind("<FocusIn>", lambda e, w=which: self._tent_focus(w))
            en.bind("<Return>", lambda e, w=which: self._tent_commit(w, True))
            en.bind("<KP_Enter>", lambda e, w=which: self._tent_commit(w, True))
            en.bind("<FocusOut>", lambda e, w=which: self._tent_commit(w, False))
            en.bind("<Escape>", lambda e, w=which: self._tent_revert(w))
            self.tvars[which], self.tents[which] = v, en
        self.time_lbl = ttk.Label(body, text="", font=("Segoe UI", 7), style="RowCardMuted.TLabel")
        self.canvas.bind("<Configure>", lambda e: (self.redraw(), self._sched_thumbs()))
        self.canvas.bind("<Button-1>", self.on_down)
        self.canvas.bind("<B1-Motion>", self.on_move)
        self.canvas.bind("<ButtonRelease-1>", lambda e: setattr(self, "drag", None))
        self.canvas.bind("<Button-3>", self.on_right)
        self.canvas.bind("<MouseWheel>", self.on_wheel)
        self.set_index(0)
        self._layout()

    def set_index(self, i):
        self.index = i
        self._update_title()

    def _hdr_down(self, e):
        self.app._reorder_pushed = False
        self._hdr_mod = bool(e.state & 0x5)                  # Shift / Ctrl: selecting, not dragging
        self.app.stop_playback()
        self.app.click_row(self, e.state)

    def _hdr_move(self, e):
        if getattr(self, "_hdr_mod", False):
            return
        if not self._hdr_drag:
            self._hdr_drag = True
            self.title_lbl.config(cursor="fleur")
            self._ghost_show()
        self._ghost_move(e)
        self.app.drag_row(self, e.y_root)

    def _hdr_up(self, e):
        if self._hdr_drag:
            self._hdr_drag = False
            self.title_lbl.config(cursor="")
            self._ghost_hide()
            self.app._reveal_row(self)

    def _ghost_show(self):
        """A small floating card (thumbnail + file name) that follows the pointer while the clip is dragged."""
        self._ghost_hide()
        try:
            pal = self.app.pal
            g = tk.Toplevel(self.app)
            g.overrideredirect(True)
            try:
                g.attributes("-topmost", True)
                g.attributes("-alpha", 0.92)
            except Exception:
                pass
            f = tk.Frame(g, bg=pal["card"], highlightthickness=2, highlightbackground=pal["accent"],
                         highlightcolor=pal["accent"])
            f.pack()
            if self.cache:
                g.img = self.cache[sorted(self.cache)[0]]
                tk.Label(f, image=g.img, bd=0, bg=pal["card"]).pack(side="left", padx=(6, 0), pady=6)
            tk.Label(f, text=f"{self.index + 1}.  {os.path.basename(self.path)}", bg=pal["card"], fg=pal["fg"],
                     font=("Segoe UI", 9, "bold"), padx=10).pack(side="left")
            self._ghost = g
        except Exception:
            self._ghost = None

    def _ghost_move(self, e):
        g = getattr(self, "_ghost", None)
        if g is not None:
            try:
                g.geometry(f"+{e.x_root + 18}+{e.y_root + 14}")
            except Exception:
                pass

    def _ghost_hide(self):
        g, self._ghost = getattr(self, "_ghost", None), None
        if g is not None:
            try:
                g.destroy()
            except Exception:
                pass

    def set_selected(self, flag):
        if flag != self.selected:
            self.selected = flag
            self._head_look()

    def _head_look(self):
        """Accent coloured title bar for the active clip and for every selected clip."""
        a = self.active or self.selected
        self.head.config(style="RowHeadA.TFrame" if a else "RowHead.TFrame")
        self.title_lbl.config(style="RowHeadA.TLabel" if a else "RowHead.TLabel")
        self.zoom_lbl.config(style="RowHeadAM.TLabel" if a else "RowHeadM.TLabel")
        for b in self.hbtns:
            b.on_accent = a
            b.recolor()
        col = self._ring_col()
        self.config(highlightbackground=col, highlightcolor=col)
        self._draw_corners()

    def set_active(self, flag):
        self.active = flag
        if not flag:
            self.sel = None
        self._update_title()
        self.apply_palette()
        self._layout()

    def _layout(self):
        """The active clip gets the full timeline; the others are shown as thin strips."""
        self._head_look()
        self.time_lbl.pack_forget()                         # (the times are drawn in the timeline)
        if self.active:
            self.tl_h, self.lane_h = TL_H + TIMES_H, LANE_H + ZLANE_H
            self.tlb = TL_H
        else:
            self.tl_h, self.lane_h = COMPACT_TL_H, COMPACT_LANE_H
            self.tlb = COMPACT_TL_H
        self.canvas.config(height=self.tl_h)
        self.redraw()

    def _ring_col(self):
        pal = self.app.pal
        return pal["accent"] if (self.active or self.selected) else pal["border"]

    def _draw_corners(self):
        pal, R = self.app.pal, self.CR
        col = self._ring_col()
        a = self.active or self.selected
        for cv, k in getattr(self, "corners", []):
            cv.delete("all")
            cv.config(bg=pal["bg"], width=R, height=R)
            inside = (pal["accent"] if a else pal["card"]) if k in ("nw", "ne") else TL_BG
            key = (k, R, pal["bg"], col, inside)
            cache = self.app.__dict__.setdefault("_corner_cache", {})
            if key not in cache:
                cache[key] = corner_image(self, k, R, pal["bg"], col, inside)
            cv.create_image(0, 0, anchor="nw", image=cache[key])
            try:
                tk.Misc.lift(cv)
            except Exception:
                pass

    def _canvas_masks(self):
        """(The timeline now fills the card - its corners are rounded by the card itself.)"""
        return
        c, pal, r = self.canvas, self.app.pal, 7
        W, H = c.winfo_width(), c.winfo_height()
        if W < 4 * r or H < 4 * r:
            return
        cache = self.app.__dict__.setdefault("_mask_cache", {})
        for k, x, y, anc in (("nw", 0, 0, "nw"), ("ne", W, 0, "ne"), ("sw", 0, H, "sw"), ("se", W, H, "se")):
            key = (k, r, pal["card"])
            if key not in cache:
                cache[key] = corner_image(self, k, r, pal["card"], None, None)
            c.create_image(x, y, anchor=anc, image=cache[key], tags="mask")

    def apply_palette(self):
        pal = self.app.pal
        col = self._ring_col()
        self.config(bg=pal["bg"], highlightbackground=col, highlightcolor=col)
        self._draw_corners()
        self.canvas.config(bg="#202020")
        for b in getattr(self, "hbtns", []):
            b.on_accent = self.active or self.selected
            b.recolor()
        for sec in self.sections:
            sec["entry"].config(bg="#dff0ff", fg="#0b2a4a", insertbackground="#0b2a4a")

    def edited_len(self):
        """Length of the clip after trimming and the speed sections."""
        try:
            v = self.vis()
            if v:
                return pieces_duration(build_pieces(self.start, self.end, [x for x, _, _ in v]))
            return self.end - self.start
        except Exception:
            return self.info["dur"]

    def _update_title(self):
        txt = (f"{'▶ ' if self.active else ''}{self.index + 1}.  {os.path.basename(self.path)}   -   "
               f"{self.info['w']}x{self.info['h']}, {self.info['fps']:.0f} fps, "
               f"{self.info['size'] / 1048576:.1f} MB, {fmt_time(self.info['dur'])}"
               f"{'   [HDR]' if self.info['trc'] else ''}"
               f"{'   [' + str(self.info.get('naudio', 1)) + ' audio tracks]' if self.info.get('naudio', 1) > 1 else ''}"
               f"   -   Length after edit: {fmt_time(self.edited_len())}")
        if txt != getattr(self, "_title_txt", None):
            self._title_txt = txt
            self.title_lbl.config(text=txt)

    def _phx(self, t):
        return max(-50, min(self.canvas.winfo_width() + 50, self._x(t)))

    def set_playhead_fast(self, t):
        self.playhead = t
        if self.view_len < self.info["dur"] - 1e-6 and not (self.view_a <= t <= self.view_a + self.view_len):
            self.set_view(t - 0.1 * self.view_len, self.view_len)          # follow the playhead when zoomed
            return
        self._place_playhead()

    def _place_playhead(self):
        c = self.canvas
        W = c.winfo_width()
        x = self._phx(self.playhead)
        c.coords("ph_line", x, 0, x, self.tlb - 8)
        c.coords("ph_tri", x - 6, 0, x + 6, 0, x, 8)
        if not self.active:                                  # inactive strip: no playhead
            c.coords("ph_line", -9, -9, -9, -8)
            c.coords("ph_tri", -9, -9, -8, -9, -9, -8)
            c.coords("ph_bg", -9, -9, -8, -8)
            c.itemconfigure("ph_txt", text="")
            return
        bx = min(max(x, PAD + 32), max(PAD + 32, W - PAD - 32))
        y = self.lane_h + 8
        c.coords("ph_bg", bx - 32, y, bx + 32, y + 16)
        c.coords("ph_txt", bx, y + 8)
        c.itemconfigure("ph_txt", text=fmt_time(self.playhead))
        if not self.app.playing and self.zooms:              # zoom bracket follows the playhead in / out of zooms
            self.app.bracket_refresh()

    # -- thumbnails (several levels of detail, cached; only the visible part is generated)
    def zoom_factor(self):
        return self.info["dur"] / max(self.view_len, 1e-6)

    def level(self):
        return max(0, math.ceil(math.log2(max(self.zoom_factor(), 1.0)) - 0.01))

    def start_thumbs(self):
        """Throws away all thumbnails and creates the visible ones again."""
        self.reset_id += 1
        self.cache.clear()
        ar = self.info["w"] / self.info["h"]
        self.tw = max(48, min(240, int(THUMB_H * ar)))
        self.n1 = self._want_n1()
        self.refresh_thumbs()

    def _want_n1(self):
        """How many thumbnails (side by side) fill the timeline at the current window width."""
        avail = max(self.canvas.winfo_width(), 800) - 2 * PAD
        return max(5, math.ceil(avail / self.tw))

    def _sched_thumbs(self):
        """Window resized: after a short pause fill the gaps (the clip you work on goes first, see ThumbPool)."""
        if not self.n1:
            return
        want = self._want_n1()
        if want <= self.n1 and want >= 0.6 * self.n1:
            return
        if self._thumb_job:
            try:
                self.after_cancel(self._thumb_job)
            except Exception:
                pass
        self._thumb_job = self.after(250 if self.active else 700, self.refresh_thumbs)

    def refresh_thumbs(self):
        self.token += 1
        if not self.n1:
            return
        want = self._want_n1()
        if want > self.n1 or want < 0.6 * self.n1:          # window got wider / much narrower: other spacing
            self.n1 = want
        n1 = self.n1
        L = self.level()
        count = n1 * (2 ** L)
        step = self.info["dur"] / count
        lo = int(max(0, math.floor((self.view_a - 0.25 * self.view_len) / step)))
        hi = int(min(count - 1, math.ceil((self.view_a + 1.25 * self.view_len) / step)))
        mid = self.view_a + self.view_len / 2
        need = sorted((k for k in range(lo, hi + 1) if (L, k, n1) not in self.cache),
                      key=lambda k: abs((k + 0.5) * step - mid))
        self._pending = len(need)
        if not need:
            self._drop_stale()
        else:
            trc = self.info["trc"] if self.app.hdr_var.get() else None
            token, rid = self.token, self.reset_id
            pool = thumb_pool()
            size = THUMB_BATCH
            if self.active:                                  # the open clip: spread over all workers
                size = max(3, min(THUMB_BATCH, math.ceil(len(need) / pool.workers)))
            chunks = [need[i:i + size] for i in range(0, len(need), size)]
            pool.submit(self, [(lambda ch=ch: self._thumb_run(token, rid, L, n1, step, ch, trc)) for ch in chunks])

    def _drop_stale(self):
        """Thumbnails made for another window width are removed once the new ones are in."""
        for kk in [kk for kk in self.cache if kk[2] != self.n1]:
            del self.cache[kk]

    def _thumb_run(self, token, rid, L, n1, step, need, trc=None):
        times = [(k + 0.5) * step for k in need]
        grab_frames(self.path, times, self.tw, THUMB_H, trc, lambda: token != self.token,
                    lambda i, data: self.after(0, self._add_thumb, rid, (L, need[i], n1), data))

    def _add_thumb(self, rid, key, data):
        if rid != self.reset_id:
            return
        if len(self.cache) > MAX_THUMB_CACHE:
            keep = {0, self.level()}
            for kk in [kk for kk in self.cache if kk[0] not in keep]:
                del self.cache[kk]
        self.cache[key] = tk.PhotoImage(data=data)
        if key[2] == self.n1:
            self._pending -= 1
            if self._pending <= 0:
                self._drop_stale()
        self.redraw()

    def detach(self):
        """Hidden but kept alive (undo can bring the clip back with its thumbnails)."""
        thumb_pool().drop(self)
        self.token += 1
        self.drag = None
        if self._thumb_job:
            try:
                self.after_cancel(self._thumb_job)
            except Exception:
                pass
            self._thumb_job = None

    def _preset_picked(self, e=None):
        labels = self.app.preset_labels()
        i = labels.index(self.preset_var.get()) if self.preset_var.get() in labels else 0
        name = None if i == 0 else self.app.presets[i - 1]["name"]
        if name != self.preset:
            self.app.push_undo("bitrate preset")
            self.preset = name
        self.app.focus_set()

    def set_preset(self, name):
        """Sets the preset by name (None / unknown name = Auto) without an undo step."""
        p = self.app.preset_by_name(name)
        self.preset = p["name"] if p else None
        self.refresh_presets()

    def refresh_presets(self):
        """The preset list changed: new combobox values; a preset that was deleted turns back to Auto."""
        if self.preset and not self.app.preset_by_name(self.preset):
            self.preset = None
        self.preset_cb.config(values=self.app.preset_labels())
        p = self.app.preset_by_name(self.preset)
        self.preset_var.set(self.app.preset_label(p))

    def set_state(self, start, end, secs, preset=None, zooms=()):
        """Restores trim markers and speed sections (undo / redo)."""
        for sec in self.sections:
            try:
                sec["entry"].destroy()
            except Exception:
                pass
        self.start, self.end = start, end
        self.sections = [self.new_section(a, b, sp) for a, b, sp in secs]
        self.sel = None
        self.zooms = [{"a": a, "b": b, "x": x, "y": y, "w": w} for a, b, x, y, w in zooms]
        self.zsel = None
        self.playhead = min(max(self.playhead, start), end)
        self.set_preset(preset)
        self.redraw()

    def dispose(self):
        thumb_pool().drop(self)
        self.token += 1
        self.reset_id += 1
        if self._thumb_job:
            try:
                self.after_cancel(self._thumb_job)
            except Exception:
                pass
        for sec in self.sections:
            try:
                sec["entry"].destroy()
            except Exception:
                pass

    # -- timeline mapping and zoom
    def _x(self, t):
        return PAD + (self.canvas.winfo_width() - 2 * PAD) * (t - self.view_a) / self.view_len

    def _t(self, x):
        w = max(1, self.canvas.winfo_width() - 2 * PAD)
        return min(max(self.view_a + (x - PAD) / w * self.view_len, 0), self.info["dur"])

    def set_view(self, a, length):
        dur = self.info["dur"]
        length = min(max(length, min(MIN_VIEW, dur)), dur)
        a = min(max(a, 0.0), dur - length)
        if abs(a - self.view_a) < 1e-9 and abs(length - self.view_len) < 1e-9:
            return
        self.view_a, self.view_len = a, length
        z = self.zoom_factor()
        self.zoom_lbl.config(text=f"zoom x{z:.1f}" if z > 1.02 else "")
        self.redraw()
        if self._thumb_job:
            try:
                self.after_cancel(self._thumb_job)
            except Exception:
                pass
        self._thumb_job = self.after(180, self.refresh_thumbs)

    def zoom_at(self, x, factor):
        w = max(1, self.canvas.winfo_width() - 2 * PAD)
        frac = min(max((x - PAD) / w, 0.0), 1.0)
        t_m = self.view_a + frac * self.view_len
        new_len = self.view_len / factor
        self.set_view(t_m - frac * new_len, new_len)

    def fit(self):
        self.set_view(0.0, self.info["dur"])

    def on_wheel(self, e):
        steps = e.delta / 120.0
        if e.state & 0x4:                                   # Ctrl + wheel: zoom around the mouse pointer
            self.zoom_at(e.x, 1.35 ** steps)
            return "break"
        if self.view_len < self.info["dur"] - 1e-6:         # zoomed in: the wheel scrolls the timeline
            self.set_view(self.view_a - steps * 0.2 * self.view_len, self.view_len)
            return "break"
        return None                                         # not zoomed: the wheel scrolls the clip list

    # -- speed sections
    def vis(self):
        """Speed sections clipped to the trimmed range (only those that are still visible)."""
        out = []
        for sec in sorted(self.sections, key=lambda x: x["a"]):
            a, b = max(sec["a"], self.start), min(sec["b"], self.end)
            if b - a >= 0.05:
                out.append((sec, a, b))
        return out

    def set_trim(self, which):
        """Hotkeys 1 / 2: the trim start / end goes to the playhead."""
        t = min(max(self.playhead, 0.0), self.info["dur"])
        if which == "start":
            if t > self.end - 0.5:
                self.app.set_status("Trim start must be at least 0.5 s before the end marker")
                return False
            self.app.push_undo("trim start")
            self.start = t
        else:
            if t < self.start + 0.5:
                self.app.set_status("Trim end must be at least 0.5 s after the start marker")
                return False
            self.app.push_undo("trim end")
            self.end = t
        self.redraw()
        self.app._renumber()
        self.app.set_status(f"Trim {which} set to {fmt_time(t)}")
        return True

    def _tent_focus(self, which):
        self.app.stop_playback()
        self.app.set_active(self)
        e = self.tents[which]
        e.after(10, lambda: e.select_range(0, "end"))

    def _tent_revert(self, which):
        self.tvars[which].set(fmt_time(self.start if which == "start" else self.end))
        self.canvas.focus_set()
        return "break"

    def _tent_commit(self, which, leave):
        t = parse_clock(self.tvars[which].get())
        cur = self.start if which == "start" else self.end
        if t is not None:
            dur = self.info["dur"]
            t = min(max(t, 0.0), dur)
            t = min(t, self.end - 0.5) if which == "start" else max(t, self.start + 0.5)
            t = min(max(t, 0.0), dur)
            if abs(t - cur) > 1e-3:
                self.app.stop_playback()
                self.app.push_undo("trim " + which)
                if which == "start":
                    self.start = t
                    self.playhead = t
                else:
                    self.end = t
                    self.playhead = max(self.start, t - 0.1)
                self.app.request_preview(self, self.playhead)
                self.app._renumber()
                self.app.set_status(f"Trim {which} set to {fmt_time(t)}")
        self.tvars[which].set(fmt_time(self.start if which == "start" else self.end))
        if leave:
            self.canvas.focus_set()
        self.redraw()
        return "break"

    @staticmethod
    def _entry_colors(speed):
        return ("#ffe0ef", "#4a0b2a") if speed < 1 else ("#dff0ff", "#0b2a4a")

    def new_section(self, a, b, speed):
        """A speed section with its multiplier entry box (not yet in the list)."""
        var = tk.StringVar(value=f"{speed:g}x")
        bg, fg = self._entry_colors(speed)
        entry = tk.Entry(self.canvas, textvariable=var, width=6, justify="center", bg=bg,
                         fg=fg, insertbackground=fg, disabledforeground=fg,
                         relief="solid", borderwidth=1, font=("Segoe UI", 9, "bold"))
        sec = {"a": a, "b": b, "speed": speed, "var": var, "entry": entry}
        entry.bind("<FocusIn>", lambda e, s=sec: self._entry_focus(s))
        entry.bind("<Return>", lambda e, s=sec: self._entry_commit(s, True))
        entry.bind("<KP_Enter>", lambda e, s=sec: self._entry_commit(s, True))
        entry.bind("<FocusOut>", lambda e, s=sec: self._entry_commit(s, False))
        entry.bind("<Escape>", lambda e, s=sec: self._entry_revert(s))
        return sec

    def vis_z(self):
        """Zoom sections clipped to the trimmed range."""
        out = []
        for z in sorted(self.zooms, key=lambda z: z["a"]):
            a, b = max(z["a"], self.start), min(z["b"], self.end)
            if b - a >= 0.05:
                out.append((z, a, b))
        return out

    def _zneighbors(self, z):
        others = [(max(o["a"], self.start), min(o["b"], self.end)) for o, _, _ in self.vis_z() if o is not z]
        prev_b = max([b for a, b in others if a < z["a"]] + [self.start])
        next_a = min([a for a, b in others if a > z["a"]] + [self.end])
        return prev_b, next_a

    def add_zoom(self):
        """Hotkey Z: puts a new zoom section (white) at the playhead (or in the next free gap)."""
        lo, hi = self.start, self.end
        t = self.playhead if lo <= self.playhead < hi else lo
        busy = [(a, b) for _, a, b in self.vis_z()]
        for a, b in busy:
            if a - 1e-6 <= t < b:
                t = b
        free, pos = [], lo
        for a, b in busy:
            if a - pos >= MIN_SECTION:
                free.append((pos, a))
            pos = max(pos, b)
        if hi - pos >= MIN_SECTION:
            free.append((pos, hi))
        gap = next(((max(g0, t), g1) for g0, g1 in free if g1 - max(g0, t) >= MIN_SECTION), None) \
            or (free[0] if free else None)
        if gap is None:
            self.app.set_status("No free space for a zoom section - trim handles / other zoom sections are in the way")
            return False
        g0, g1 = gap
        length = min(max(2.0, 0.1 * min(hi - lo, self.view_len)), g1 - g0)
        self.app.push_undo("add zoom section")
        z = {"a": g0, "b": g0 + length, "x": 0.25, "y": 0.25, "w": 0.5}
        self.zooms.append(z)
        self.zsel, self.sel = z, None
        self.app.stop_playback()
        self.app.set_active(self)
        self.playhead = z["a"]
        self.canvas.focus_set()
        self.app.request_preview(self, self.playhead)
        self.redraw()
        self.app.set_status("Zoom section added - drag the white bracket on the preview to choose the area "
                            "(corners = size)")
        return True

    def remove_zoom(self, z):
        if z is None or z not in self.zooms:
            return
        self.app.stop_playback()
        self.app.push_undo("remove zoom section")
        self.zooms.remove(z)
        if self.zsel is z:
            self.zsel = None
        self.redraw()

    def add_section(self, speed=2.0):
        """Hotkey S / D: puts a new speed section (D: slow motion) at the playhead (or in the next free gap)."""
        lo, hi = self.start, self.end
        t = self.playhead if lo <= self.playhead < hi else lo
        busy = [(a, b) for _, a, b in self.vis()]
        for a, b in busy:                                   # playhead inside a section -> start after it
            if a - 1e-6 <= t < b:
                t = b
        free, pos = [], lo
        for a, b in busy:
            if a - pos >= MIN_SECTION:
                free.append((pos, a))
            pos = max(pos, b)
        if hi - pos >= MIN_SECTION:
            free.append((pos, hi))
        gap = next(((max(g0, t), g1) for g0, g1 in free if g1 - max(g0, t) >= MIN_SECTION), None) \
            or (free[0] if free else None)
        if gap is None:
            self.app.set_status("No free space for a speed section - trim handles / other sections are in the way")
            return False
        g0, g1 = gap
        length = min(max(1.0, 0.1 * min(hi - lo, self.view_len)), g1 - g0)
        sec = self.new_section(g0, g0 + length, speed)
        self.app.push_undo("add speed section")
        self.sections.append(sec)
        self.sel = sec
        self.zsel = None
        self.app.stop_playback()
        self.app.set_active(self)
        self.playhead = sec["a"]
        self.canvas.focus_set()
        self.app.request_preview(self, self.playhead)
        self.redraw()
        self.app.set_status("Slow-motion section added - type a multiplier in the pink box (e.g. 0.25 = four times slower)" if speed < 1
                            else "Speed section added - type a multiplier in the blue box (e.g. 4 = four times faster)")
        return True

    def remove_section(self, sec):
        if sec is None or sec not in self.sections:
            return
        self.app.stop_playback()
        self.app.push_undo("remove speed section")
        self.sections.remove(sec)
        try:
            sec["entry"].destroy()
        except Exception:
            pass
        if self.sel is sec:
            self.sel = None
        self.redraw()

    def _entry_focus(self, sec):
        self.sel = sec
        self.app.stop_playback()
        self.app.set_active(self)
        sec["entry"].after(10, lambda: sec["entry"].select_range(0, "end"))

    def _entry_revert(self, sec):
        sec["var"].set(f"{sec['speed']:g}x")
        self.canvas.focus_set()
        return "break"

    def _entry_commit(self, sec, leave):
        txt = sec["var"].get().strip().lower().replace(",", ".").replace("×", "").replace("x", "").strip()
        try:
            v = float(txt)
            if not v > 0:
                raise ValueError
            if abs(clamp_speed(v) - sec["speed"]) > 1e-9:
                self.app.push_undo("speed multiplier")
            sec["speed"] = clamp_speed(v)
        except ValueError:
            pass                                            # keep the old value
        sec["var"].set(f"{sec['speed']:g}x")
        self.app.stop_playback()
        self.redraw()
        if leave:
            self.canvas.focus_set()
        return "break"

    def on_right(self, e):
        t = self._t(e.x)
        if self.active and LANE_H <= e.y < self.lane_h:
            for z, a, b in self.vis_z():
                if a <= t <= b:
                    self.remove_zoom(z)
                    return
        for sec, a, b in self.vis():
            if a <= t <= b:
                self.remove_section(sec)
                return

    # -- drawing
    def redraw(self):
        try:
            self.app._update_total()
        except Exception:
            pass
        c = self.canvas
        c.delete("all")
        W = c.winfo_width()
        y0, y1 = self.lane_h + 4, self.tlb - 8
        act = self.active
        if not act:
            y0, y1 = 0, self.tlb                           # strip: markers span the full thumbnail height
        Lx, Rx = PAD, W - PAD
        cl = lambda x: min(max(x, Lx), Rx)                  # keep all coordinates near the canvas
        for key in sorted(self.cache, key=lambda k_: (k_[2], k_[0], k_[1])):
            x = self._x((key[1] + 0.5) * self.info["dur"] / (key[2] * 2 ** key[0]))
            if -self.tw < x < W + self.tw:
                c.create_image(x, (y0 + y1) / 2, image=self.cache[key], anchor="center")
        xs, xe = self._x(self.start), self._x(self.end)
        x00, x11 = self._x(0.0), self._x(self.info["dur"])   # the video itself: nothing is shown outside it
        if x00 > 0:
            c.create_rectangle(0, y0, x00, y1, fill="#202020", outline="")
        if x11 < W:
            c.create_rectangle(x11, y0, W, y1, fill="#202020", outline="")
        mid = (y0 + y1) / 2                                  # the thumbnails are THUMB_H tall, centred in the strip
        ty0, ty1 = max(y0, mid - THUMB_H / 2), min(y1, mid + THUMB_H / 2)
        if xs > max(x00, 0):                                 # dark = trimmed away (only over the video itself)
            c.create_rectangle(max(x00, 0), ty0, min(xs, W), ty1, fill="#000", stipple="gray75", outline="")
        if xe < min(x11, W):
            c.create_rectangle(max(xe, 0), ty0, min(x11, W), ty1, fill="#000", stipple="gray75", outline="")
        if act and self.zoom_factor() > 1.05:                # time marks
            pps = (Rx - Lx) / self.view_len
            step = next((st for st in TICK_STEPS if st * pps >= 90), TICK_STEPS[-1])
            tt = math.ceil(self.view_a / step) * step
            while tt <= self.view_a + self.view_len + 1e-6:
                x = self._x(tt)
                c.create_line(x, y1 - 7, x, y1, fill="#fff")
                c.create_text(x + 4, y1 - 10, text=fmt_tick(tt), anchor="w", fill="#000", font=("Segoe UI", 7))
                c.create_text(x + 3, y1 - 11, text=fmt_tick(tt), anchor="w", fill="#fff", font=("Segoe UI", 7))
                tt += step
        for sec, a, b in self.vis():                        # speed sections (blue)
            xa, xb = self._x(a), self._x(b)
            if xb < Lx - 6 or xa > Rx + 6:
                continue
            ca, cb = cl(xa), cl(xb)
            on = sec is self.sel                              # selected: the whole bar turns light cyan / light pink
            slow = sec["speed"] < 1
            col = (("#fbcfe8", "#f472b6", "#fdf2f8", "#fce7f3") if on else ("#ec4899", "#ec4899", "#db2777", "#f9a8d4")) \
                if slow else (("#5eead4", "#22d3ee", "#ecfeff", "#cffafe") if on else ("#3b82f6", "#3b82f6", "#2563eb", "#93c5fd"))
            c.create_rectangle(ca, y0, cb, y1, fill=col[0], stipple="gray50" if on else "gray25", outline="")
            if act:
                c.create_rectangle(ca, 4, cb, LANE_H - 4, fill=col[1], outline=col[2], width=2 if on else 1)
                bg, fg = self._entry_colors(sec["speed"])
                sec["entry"].configure(bg=bg, fg=fg, insertbackground=fg, disabledforeground=fg)
            for xx in (xa, xb):
                if Lx - 6 <= xx <= Rx + 6:
                    c.create_line(xx, 8 if act else y0, xx, self.tlb - 14 if act else y1, width=4 if act else 3,
                                  capstyle="round", fill=col[3])
            if act:
                cx = min(max((ca + cb) / 2, PAD + 30), max(PAD + 30, W - PAD - 30))
                c.create_window(cx, LANE_H / 2, window=sec["entry"], width=54, height=20)
        for z, a, b in self.vis_z():                          # zoom sections (white)
            xa, xb = self._x(a), self._x(b)
            if xb < Lx - 6 or xa > Rx + 6:
                continue
            ca, cb = cl(xa), cl(xb)
            on = z is self.zsel
            c.create_rectangle(ca, y0, cb, y1, fill="#ffffff", stipple="gray25" if on else "gray12", outline="")
            if act:
                ly0, ly1 = LANE_H + 1, self.lane_h - 2
                c.create_rectangle(ca, ly0, cb, ly1, fill="#ffffff" if on else "#8e8e96",
                                   outline="#111111" if on else "#55555c", width=2 if on else 1)
                if cb - ca > 40:
                    c.create_text((ca + cb) / 2, (ly0 + ly1) / 2, text="ZOOM", fill="#111111",
                                  font=("Segoe UI", 7, "bold"))
                for xx in (xa, xb):
                    if Lx - 6 <= xx <= Rx + 6:
                        c.create_line(xx, ly0 - 2, xx, self.tlb - 14, width=4, capstyle="round",
                                      fill="#ffffff" if on else "#a1a1aa")
            else:
                c.create_rectangle(ca, y0, cb, y0 + 3, fill="#ffffff", outline="")
        for xx, colr, d in ((xs, "#22c55e", -1), (xe, "#ef4444", 1)):      # trim handles: bar + flags pointing outwards
            if Lx - 6 <= xx <= Rx + 6:
                bw = 6 if act else 4                       # bar width
                fl = 7 if act else 5                       # flag size
                ya, yb = ty0, ty1                          # same height as the thumbnails
                h = bw / 2.0
                xo = xx + d * h                            # outer edge of the bar
                c.create_rectangle(xx - h, ya, xx + h, yb, fill=colr, outline="")
                c.create_polygon(xo, ya, xo + d * fl, ya, xo, ya + fl, fill=colr, outline="")
                c.create_polygon(xo, yb, xo + d * fl, yb, xo, yb - fl, fill=colr, outline="")
        if act:                                              # time boxes: only for a handle that has been moved
            dur = self.info["dur"]
            for which, xx, d, moved in (("start", xs, -1, self.start > 0.005), ("end", xe, 1, self.end < dur - 0.005)):
                if not moved or not (Lx - 6 <= xx <= Rx + 6):
                    continue
                en, ew = self.tents[which], 60
                if self.canvas.focus_get() is not en:
                    self.tvars[which].set(fmt_time(self.start if which == "start" else self.end))
                cx = xx + d * (3 + 5 + ew / 2)
                if cx - ew / 2 < 1 or cx + ew / 2 > W - 1:    # no room on the outside: put it inside
                    cx = xx - d * (3 + 5 + ew / 2)
                c.create_window(cx, mid, window=en, width=ew, height=20)
        if act and self.zoom_factor() > 1.05:                # overview bar: where the visible part is
            dur = self.info["dur"]
            c.create_rectangle(Lx, self.tlb - 7, Rx, self.tlb - 2, fill="#444", outline="")
            c.create_rectangle(Lx + (Rx - Lx) * self.view_a / dur, self.tlb - 7,
                               Lx + (Rx - Lx) * (self.view_a + self.view_len) / dur, self.tlb - 2,
                               fill="#bbb", outline="")
        c.create_line(0, 0, 0, 0, fill="#fbbf24", width=2, tags=("ph", "ph_line"))
        c.create_polygon(0, 0, 0, 0, 0, 0, fill="#fbbf24", outline="", tags=("ph", "ph_tri"))
        c.create_rectangle(0, 0, 0, 0, fill="#111111", outline="#fbbf24", tags=("ph", "ph_bg"))
        c.create_text(0, 0, text="", fill="#fbbf24", font=("Segoe UI", 8, "bold"), tags=("ph", "ph_txt"))
        self._place_playhead()
        self._canvas_masks()
        self._update_title()
        if act:
            try:
                self.app.bracket_refresh()
            except Exception:
                pass

    def on_down(self, e):
        self.app.stop_playback()
        was_compact = not self.active
        if (e.state & 0x5) and was_compact:                  # Shift / Ctrl + click: only changes the selection
            self.app.click_row(self, e.state)
            return
        if was_compact or len(self.app.selection) > 1:
            self.app.click_row(self, 0)
        self.canvas.focus_set()
        if was_compact:                                      # the strip grows: first click just selects + seeks
            self.drag = "play"
            self.on_move(e)
            return
        if self.zoom_factor() > 1.05 and e.y >= self.tlb - 10:    # overview bar: drag to scroll
            self.drag = ("ov",)
            self.on_move(e)
            return
        t = self._t(e.x)
        if LANE_H <= e.y < self.lane_h:                      # zoom lane
            zc = []
            for z, a, b in self.vis_z():
                zc.append((abs(e.x - self._x(a)), ("za", z)))
                zc.append((abs(e.x - self._x(b)), ("zb", z)))
            zb = min(zc, key=lambda c_: c_[0]) if zc else None
            hit = next(((z, a, b) for z, a, b in self.vis_z() if a <= t <= b), None)
            if zb and zb[0] <= HANDLE_GRAB:
                self.drag = zb[1]
                z = self.drag[1]
                z["a"], z["b"] = max(z["a"], self.start), min(z["b"], self.end)
                self.zsel, self.sel = z, None
            elif hit:
                z, a, b = hit
                z["a"], z["b"] = a, b
                self.zsel, self.sel = z, None
                self.drag = ("zmove", z, t - a)
            else:
                self.zsel, self.sel = None, None
                self.drag = "play"
            if self.drag != "play":
                self.app.push_undo("zoom section")
            self._snap_setup()
            self.on_move(e)
            self.app.bracket_refresh()
            return
        in_lane = e.y < self.lane_h
        cands = [(abs(e.x - self._x(self.start)) + (6 if in_lane else 0), "start"),
                 (abs(e.x - self._x(self.end)) + (6 if in_lane else 0), "end")]
        vis = self.vis()
        for sec, a, b in vis:
            cands.append((abs(e.x - self._x(a)), ("sa", sec)))
            cands.append((abs(e.x - self._x(b)), ("sb", sec)))
        for z, a, b in self.vis_z():                         # the zoom bars run through the strip too
            cands.append((abs(e.x - self._x(a)) - 0.5, ("za", z)))
            cands.append((abs(e.x - self._x(b)) - 0.5, ("zb", z)))
        best = min(cands, key=lambda c: c[0])
        self.drag = None
        if best[0] <= HANDLE_GRAB:
            self.drag = best[1]
            if isinstance(self.drag, tuple):
                sec = self.drag[1]
                sec["a"], sec["b"] = max(sec["a"], self.start), min(sec["b"], self.end)
                if self.drag[0] in ("za", "zb"):
                    self.zsel, self.sel = sec, None
                else:
                    self.sel = sec
                    self.zsel = None
        else:
            hit = next(((sec, a, b) for sec, a, b in vis if a <= t <= b), None)
            self.sel = hit[0] if hit else None
            if hit:
                self.zsel = None
            if hit and in_lane:
                sec, a, b = hit
                sec["a"], sec["b"] = a, b
                self.drag = ("move", sec, t - a)
            else:
                self.drag = "play"
        if self.drag is None:
            self.drag = "play"
        if self.drag != "play":
            self.app.push_undo("trim" if self.drag in ("start", "end")
                               else "zoom section" if self.drag[0] in ("za", "zb") else "speed section")
        self._snap_setup()
        self.on_move(e)

    SNAP_PX = 9                                              # how close (in pixels) a bar snaps to the playhead

    def _snap_setup(self):
        """The bars snap to the yellow line where playback was stopped (not to the bar's own position)."""
        d, pt = self.drag, self.playhead
        own = []
        if d == "start":
            own = [self.start]
        elif d == "end":
            own = [self.end]
        elif isinstance(d, tuple) and d[0] == "sa":
            own = [d[1]["a"]]
        elif isinstance(d, tuple) and d[0] == "sb":
            own = [d[1]["b"]]
        elif isinstance(d, tuple) and d[0] in ("move", "zmove"):
            own = [d[1]["a"], d[1]["b"]]
        elif isinstance(d, tuple) and d[0] == "za":
            own = [d[1]["a"]]
        elif isinstance(d, tuple) and d[0] == "zb":
            own = [d[1]["b"]]
        self.snap_t = None if (d == "play" or isinstance(d, tuple) and d[0] == "ov" or
                               any(abs(pt - o) < 1e-3 for o in own)) else pt

    def _snap(self, t, *offsets):
        """t (and t + each offset) is pulled onto the snap position when it is within a few pixels."""
        st = getattr(self, "snap_t", None)
        if st is None:
            return t
        pps = max(1, self.canvas.winfo_width() - 2 * PAD) / self.view_len
        for off in (0.0,) + offsets:
            if abs(t + off - st) * pps <= self.SNAP_PX:
                return st - off
        return t

    def _neighbors(self, sec):
        others = [(max(o["a"], self.start), min(o["b"], self.end)) for o, _, _ in self.vis() if o is not sec]
        prev_b = max([b for a, b in others if a < sec["a"]] + [self.start])
        next_a = min([a for a, b in others if a > sec["a"]] + [self.end])
        return prev_b, next_a

    def on_move(self, e):
        if not self.drag:
            return
        d = self.drag
        if d[0] == "ov":
            w = max(1, self.canvas.winfo_width() - 2 * PAD)
            self.set_view((e.x - PAD) / w * self.info["dur"] - self.view_len / 2, self.view_len)
            return
        W = self.canvas.winfo_width()
        if self.view_len < self.info["dur"] - 1e-6 and (e.x < PAD or e.x > W - PAD):
            self.set_view(self.view_a + (-1 if e.x < PAD else 1) * 0.04 * self.view_len, self.view_len)   # edge scroll
        t = self._t(e.x)
        if d in ("start", "end") or (isinstance(d, tuple) and d[0] in ("sa", "sb", "za", "zb")):
            t = self._snap(t)
        if d == "start":
            self.start = max(0.0, min(t, self.end - 0.5))
            self.playhead = self.start
        elif d == "end":
            self.end = min(self.info["dur"], max(t, self.start + 0.5))
            self.playhead = max(self.start, self.end - 0.1)
        elif d == "play":
            self.playhead = t
        elif d[0] in ("za", "zb", "zmove"):
            z = d[1]
            lo, hi = self._zneighbors(z)
            if d[0] == "za":
                z["a"] = min(max(t, lo), z["b"] - MIN_SECTION)
                self.playhead = z["a"]
            elif d[0] == "zb":
                z["b"] = max(min(t, hi), z["a"] + MIN_SECTION)
                self.playhead = max(z["a"], z["b"] - 0.05)
            else:
                w = z["b"] - z["a"]
                if hi - lo >= w:
                    z["a"] = min(max(self._snap(t - d[2], w), lo), hi - w)
                    z["b"] = z["a"] + w
                self.playhead = z["a"]
        else:
            sec = d[1]
            lo, hi = self._neighbors(sec)
            if d[0] == "sa":
                sec["a"] = min(max(t, lo), sec["b"] - MIN_SECTION)
                self.playhead = sec["a"]
            elif d[0] == "sb":
                sec["b"] = max(min(t, hi), sec["a"] + MIN_SECTION)
                self.playhead = max(sec["a"], sec["b"] - 0.05)
            else:                                           # move the whole section
                w = sec["b"] - sec["a"]
                if hi - lo >= w:
                    sec["a"] = min(max(self._snap(t - d[2], w), lo), hi - w)
                    sec["b"] = sec["a"] + w
                self.playhead = sec["a"]
        self.app.request_preview(self, self.playhead)
        self.redraw()

    def clip(self):
        i = self.info
        return {"path": self.path, "start": self.start, "end": self.end, "w": i["w"], "h": i["h"],
                "fps": i["fps"], "audio": i["audio"], "naudio": i.get("naudio", 1 if i["audio"] else 0),
                "bitrate": i["bitrate"],
                "trc": i["trc"], "prim": i["prim"], "space": i["space"],
                "pieces": (build_pieces(self.start, self.end, [x for x, _, _ in self.vis()])
                           if self.vis() else None),
                "zooms": [(a, b, z["x"], z["y"], z["w"]) for z, a, b in self.vis_z()]}


# --------------------------------------------------------------------- app
class App(tk.Tk):
    def __init__(self):
        try:                                                # own taskbar identity (not "Python")
            import ctypes
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(APP_ID)
        except Exception:
            pass
        super().__init__()
        try:
            self.attributes("-alpha", 0.0)                  # invisible while the theme is being set up
        except Exception:
            pass
        self.settings = load_settings()
        self.pal, self.theme = PALETTE["dark"], "dark"
        self._init_theme()
        self.title(f"{APP_NAME} v{APP_VERSION}")
        try:
            self.iconbitmap(default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "clipshrinker.ico"))
        except Exception:
            pass
        self.minsize(1150, 640)
        self._normal_geo = None
        self._restore_geometry()
        self.rows = []
        self.preview_img = None
        self._pending_preview = None
        self._preview_busy = False
        self._last_preview = None
        self.encoder = None
        self.encoding = False
        self.active_row = None
        self.restore_var = tk.BooleanVar(value=self._saved("restore_session", False))
        self.prefix_var = tk.StringVar(value=self.settings.get("prefix", "") if isinstance(self.settings.get("prefix", ""), str) else "")
        self.suffix_var = tk.StringVar(value=self.settings.get("suffix", "") if isinstance(self.settings.get("suffix", ""), str) else "")
        self.save_dir_var = tk.StringVar(value=self.settings.get("save_dir", "") if isinstance(self.settings.get("save_dir", ""), str) else "")
        self.keys = dict(KEY_DEFAULTS)
        sk = self.settings.get("keys")
        if isinstance(sk, dict):
            for a_, v_ in sk.items():
                if a_ in KEY_DEFAULTS and isinstance(v_, str) and v_ and v_ not in RESERVED_PLAIN and v_ not in RESERVED_CTRL:
                    self.keys[a_] = v_
        if len(set(self.keys.values())) < len(self.keys):                     # a damaged / clashing file: back to default
            self.keys = dict(KEY_DEFAULTS)
        self.project_path = None
        self.selection = set()
        self.undo_stack, self.redo_stack, self._detached = [], [], set()
        self._undo_off, self._undo_serial = False, 0
        self._reorder_pushed = False
        self.presets = load_presets()
        self.hdr_var = tk.BooleanVar(value=self._saved("hdr", True))
        self.fast_aud_var = tk.StringVar(value=self._saved("fast_audio", FAST_AUDIO[0], FAST_AUDIO))
        self.slow_aud_var = tk.StringVar(value=self._saved("slow_audio", SLOW_AUDIO[0], SLOW_AUDIO))
        self.fast_vol_var = tk.IntVar(value=self._saved_int("fast_vol", 70))
        self.slow_vol_var = tk.IntVar(value=self._saved_int("slow_vol", 150))
        for _v, _k in ((self.fast_vol_var, "fast_vol"), (self.slow_vol_var, "slow_vol")):
            AUD[_k] = _v.get()
            _v.trace_add("write", lambda *a, v=_v, k=_k: (AUD.__setitem__(k, int(v.get())), self.stop_playback()))
        self.zoom_fx_var = tk.StringVar(value=self._saved("zoom_fx", ZOOM_FX[0], ZOOM_FX))
        self.zoom_snd_var = tk.StringVar(value=self._saved("zoom_snd", ZOOM_AUDIO[0], ZOOM_AUDIO))
        self.zoom_len_var = tk.StringVar(value=str(self.settings.get("zoom_len", "0.4")))
        self.zoom_vol_var = tk.IntVar(value=self._saved_int("zoom_vol", 80))

        def _zoom_sync(*a):
            ZOOM["motion"] = self.zoom_fx_var.get() == ZOOM_FX[0]
            ZOOM["sound"] = self.zoom_snd_var.get() == ZOOM_AUDIO[0]
            try:
                ZOOM["len"] = max(0.1, min(2.0, float(self.zoom_len_var.get().replace(",", "."))))
            except ValueError:
                pass
            ZOOM["vol"] = int(self.zoom_vol_var.get())
            if hasattr(self, "playing"):
                self.stop_playback()
        for _v in (self.zoom_fx_var, self.zoom_snd_var, self.zoom_len_var, self.zoom_vol_var):
            _v.trace_add("write", _zoom_sync)
        _zoom_sync()
        self.pitch_var = tk.StringVar(value=self._saved("slow_pitch", SLOW_PITCH[0], SLOW_PITCH))
        AUD["deep"] = self.pitch_var.get() == SLOW_PITCH[0]
        self.fast_pitch_var = tk.StringVar(value=self._saved("fast_pitch", FAST_PITCH[1], FAST_PITCH))
        AUD["high"] = self.fast_pitch_var.get() == FAST_PITCH[0]
        self.fast_pitch_var.trace_add("write", lambda *a: (AUD.__setitem__("high", self.fast_pitch_var.get() == FAST_PITCH[0]), self.stop_playback()))
        self.pitch_var.trace_add("write", lambda *a: (AUD.__setitem__("deep", self.pitch_var.get() == SLOW_PITCH[0]), self.stop_playback()))
        self.vhs_var = tk.BooleanVar(value=self._saved("vhs", True))
        self.interp_var = tk.StringVar(value=self._saved("slow_interp", SLOW_INTERP[2], SLOW_INTERP))
        self.rife_busy = ""
        self.rife_pct = 0
        for _v, _d, _k in ((self.vhs_var, VHS, "on"),):
            _d[_k] = bool(_v.get())
            _v.trace_add("write", lambda *a, v=_v, d=_d, k=_k: d.__setitem__(k, bool(v.get())))
        def _interp(*a):
            m = interp_key(self.interp_var.get())
            SLOW["blend"], SLOW["rife"] = m != "repeat", m == "rife"
            if hasattr(self, "playing"):
                self.stop_playback()
            if hasattr(self, "rife_lbl"):
                self._rife_refresh()
        _interp()
        self.interp_var.trace_add("write", _interp)
        for _v, _k in ((self.fast_aud_var, "fast"), (self.slow_aud_var, "slow")):
            AUD[_k] = aud_key(_v.get())
            _v.trace_add("write", lambda *a, v=_v, k=_k: (AUD.__setitem__(k, aud_key(v.get())), self.stop_playback()))
        self.atrk_var = tk.StringVar(value=self._saved("atrk", "Mix into one track",
                                                      ["Keep separate tracks", "Mix into one track"]))
        self.mutemic_var = tk.BooleanVar(value=self._saved("mute_mic", False))
        self.playing = False
        self.play_gen = 0
        self.play_proc = None
        self.play_row = None
        self._play_lvl = 2          # 2 = GPU decode+scale, 1 = GPU decode, 0 = software
        self._snd_lock = threading.Lock()
        self._wave = None
        self._wave_ok = os.name == "nt"
        self._wcache = {}
        self._play_dims = (0, 0)
        self._frame = None
        self._draw_pending = False
        self._frame_lock = threading.Lock()
        self._resize_job = None

        self._build_ui()
        self.bind("<space>", self._on_space)
        self.bind("<Control-o>", lambda e: self.open_project())
        self.bind("<Control-s>", lambda e: self.save_project())
        self.bind("<Control-S>", lambda e: self.save_project(True))
        self.bind("<Control-z>", lambda e: None if self._typing() else self.undo())
        self.bind("<Control-y>", lambda e: None if self._typing() else self.redo())
        self.bind("<Control-Z>", lambda e: None if self._typing() else self.redo())
        self.bind("<Delete>", self._on_delete)
        self.bind("<BackSpace>", self._on_backspace)
        self.bind("<Control-a>", lambda e: None if self._typing() else self.select_all())
        self.bind("<m>", self._on_mute)
        self.bind("<M>", self._on_mute)
        self.protocol("WM_DELETE_WINDOW", self.on_close)
        self.after(100, self._check_tools)
        if self.restore_var.get():
            self.after(500, self._restore_session)
        self.after(200, self._enable_file_drop)
        self.after(120, lambda: self._titlebar(self.theme == "dark"))
        self.after(300, self._finish_start)
        self.after(2500, self._reveal)                      # safety net
        self.bind("<Configure>", self._on_win_configure)
        self._bitrate_changed()
        self._mode_changed()

    # ---------------------------------------------- saved settings & window position
    def _saved_int(self, key, default):
        v = self.settings.get(key, default)
        return max(0, min(200, v)) if isinstance(v, int) and not isinstance(v, bool) else default

    def _saved(self, key, default, allowed=None):
        v = self.settings.get(key, default)
        if isinstance(default, bool):
            return v if isinstance(v, bool) else default
        if not isinstance(v, str):
            return default
        return v if (allowed is None or v in allowed) else default

    def _on_win_configure(self, e):
        if e.widget is self:
            try:
                if self.state() == "normal":
                    self._normal_geo = self.geometry()
            except Exception:
                pass

    def _virtual_screen(self):
        """Left, top, width, height of the whole desktop (all monitors)."""
        try:
            import ctypes
            u = ctypes.windll.user32
            return u.GetSystemMetrics(76), u.GetSystemMetrics(77), u.GetSystemMetrics(78), u.GetSystemMetrics(79)
        except Exception:
            return 0, 0, self.winfo_screenwidth(), self.winfo_screenheight()

    def _restore_geometry(self):
        import re
        vx, vy, vw, vh = self._virtual_screen()
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        w, h, x, y = 1500, 940, None, None
        m = re.fullmatch(r"(\d+)x(\d+)\+(-?\d+)\+(-?\d+)", str(self.settings.get("geometry", "")))
        if m:
            w, h, x, y = (int(g) for g in m.groups())
        w, h = max(900, min(w, vw)), max(640, min(h, vh - 60))
        on_screen = x is not None and vx - 50 <= x + 150 <= vx + vw and vy - 10 <= y <= vy + vh - 80
        if not on_screen:                                   # first start / monitor gone: center on the main screen
            x, y = max(0, (sw - w) // 2), max(0, (sh - h) // 2 - 20)
        self.geometry(f"{w}x{h}+{x}+{y}")
        if self.settings.get("zoomed") is True:
            self.after(60, lambda: self.state("zoomed"))

    PANE_MIN_TOP, PANE_MIN_LIST = 140, 230

    def _clamp_sash(self, e=None):
        try:
            H = self.paned.winfo_height()
            if H < 50:
                return
            top_min = self.PANE_MIN_TOP + self.ctl_row.winfo_reqheight() + 20
            hi = max(top_min, H - self.PANE_MIN_LIST)
            pos = self.paned.sashpos(0)
            new = min(max(pos, top_min), hi)
            if new != pos:
                self.paned.sashpos(0, new)
        except Exception:
            pass

    def _restore_sash(self):
        pos = self.settings.get("sash")
        if isinstance(pos, int) and pos > 150:
            try:
                self.paned.sashpos(0, min(pos, max(200, self.paned.winfo_height() - 120)))
            except Exception:
                pass

    def save_all_settings(self):
        d = self.settings
        try:
            d.update({
                "theme": self.theme, "size": self.size_var.get(), "bitrate": self.bitrate_var.get(),
                "crop": self.crop_var.get(), "res": self.res_var.get(),
                "res_before_bitrate": self._res_before_bitrate, "codec": self.codec_var.get(),
                "audio": self.audio_var.get(), "atrk": self.atrk_var.get(), "mute_mic": bool(self.mutemic_var.get()),
                "fast_audio": self.fast_aud_var.get(), "slow_audio": self.slow_aud_var.get(), "slow_pitch": self.pitch_var.get(), "fast_pitch": self.fast_pitch_var.get(), "vhs": bool(self.vhs_var.get()),
                "fast_vol": int(self.fast_vol_var.get()), "slow_vol": int(self.slow_vol_var.get()),
                "zoom_fx": self.zoom_fx_var.get(), "zoom_snd": self.zoom_snd_var.get(), "zoom_len": self.zoom_len_var.get(), "zoom_vol": int(self.zoom_vol_var.get()),
                "slow_interp": self.interp_var.get(), "hdr": bool(self.hdr_var.get()), "mode": self.mode_var.get(),
                "trans": self.trans_var.get(), "tlen": self.tlen_var.get(), "sound": bool(self.sound_var.get()), "pv_vol": int(self.pv_vol_var.get()),
                "zoomed": self.state() == "zoomed", "restore_session": bool(self.restore_var.get()),
                "prefix": self.prefix_var.get(), "suffix": self.suffix_var.get(), "save_dir": self.save_dir_var.get(), "keys": dict(self.keys),
            })
            if self._normal_geo:
                d["geometry"] = self._normal_geo
            try:
                d["sash"] = int(self.paned.sashpos(0))
            except Exception:
                pass
        except Exception:
            if _LOG:
                traceback.print_exc(file=_LOG)
        save_settings(d)

    def _finish_start(self):
        try:
            self._theme_refresh()
            self.update_idletasks()
            self._taskbar_props()
            self._restore_sash()
            self.update_idletasks()
        finally:
            self.after(180, self._reveal)

    def _taskbar_props(self):
        if sys.platform != "win32":
            return
        try:
            import ctypes
            exe = os.path.normpath(os.path.join(APP_DIR, "..", "ClipShrinker.exe"))
            if not os.path.isfile(exe):
                return
            user32 = ctypes.windll.user32
            user32.GetParent.restype = ctypes.c_void_p
            hwnd = user32.GetParent(self.winfo_id())
            set_window_taskbar_props(hwnd, exe, "Clip Shrinker")
        except Exception:
            if _LOG:
                traceback.print_exc(file=_LOG)

    def _reveal(self):
        self._restore_tk_colors()
        try:
            self.attributes("-alpha", 1.0)
        except Exception:
            pass
        self.after(700, self._prerender_settings)

    def _prerender_settings(self):
        """Maps the Settings window once (invisible) at start-up, so its first real opening is quick."""
        win = getattr(self, "settings_win", None)
        try:
            if win is None or win.winfo_viewable():
                return
            win.attributes("-alpha", 0.0)
            win.geometry("+-3000+-3000")
            win.deiconify()
            win.update_idletasks()
            self._titlebar(self.theme == "dark", win)
            win.withdraw()
        except tk.TclError:
            pass

    # ------------------------------------------------------------- theme
    def _theme_refresh(self):
        """Re-applies the theme once the window exists (fixes a half-applied look on the first start)."""
        other = "light" if self.theme == "dark" else "dark"
        cur = self.theme
        self.apply_theme(other, save=False)
        self.apply_theme(cur, save=False)

    def _init_theme(self):
        theme = self.settings.get("theme") or system_theme()
        if theme not in PALETTE:
            theme = "dark"
        self.theme_var = tk.BooleanVar(value=(theme == "dark"))
        other = "light" if theme == "dark" else "dark"      # go through the other theme once: a plain first
        self.apply_theme(other, save=False)                 # set_theme left some widgets un-themed on Windows
        self.apply_theme(theme, save=False)

    def sty(self, name):
        """Style name when the theme provides it, else '' (= default look)."""
        try:
            if ttk.Style(self).layout(name):
                return name
        except Exception:
            pass
        return ""

    def _danger_style(self, st, theme):
        """Style 'Danger.TButton': the rounded theme button in red (images are made for each theme once)."""
        if sv_ttk is None:
            return
        try:
            imgs = getattr(self, "_danger_imgs", None)
            if imgs is None:
                imgs = self._danger_imgs = {}
            if theme not in imgs:
                imgs[theme] = {k: tk.PhotoImage(data=v, format="png") for k, v in DANGER_SPRITES[theme].items()}
            im = imgs[theme]
            st.element_create("DangerButton.button", "image", str(im["rest"]),
                              ("pressed", str(im["pressed"])), ("active", "focus", str(im["focus-hover"])),
                              ("active", str(im["hover"])), ("focus", str(im["focus"])),
                              border=4, sticky="nsew")
            st.layout("Danger.TButton", [("DangerButton.button", {"children": [
                ("DangerButton.padding", {"children": [("DangerButton.label", {"side": "left", "expand": 1})]})]})])
            st.configure("Danger.TButton", padding=(8, 2, 8, 3), anchor="center", foreground="#ffffff")
            st.map("Danger.TButton", foreground=[("pressed", "#ffd5d5"), ("disabled", "#bbbbbb")])
        except Exception:
            if _LOG:
                traceback.print_exc(file=_LOG)

    def apply_theme(self, theme, save=True):
        self.theme, self.pal = theme, PALETTE[theme]
        pal = self.pal
        try:
            if sv_ttk is not None:
                sv_ttk.set_theme(theme)
            st = ttk.Style(self)
            self._danger_style(st, theme)
            st.configure("Muted.TLabel", foreground=pal["muted"])
            st.configure("Sash", sashthickness=4, gripcount=30)      # longer grip (theme: 4 / 20)
            st.configure("SmallMuted.TLabel", foreground=pal["muted"])
            sf = getattr(self, "_small_font", None)             # (the theme resets the summary font: put it back)
            if sf:
                st.configure("Small.TLabel", font=sf)
                st.configure("SmallMuted.TLabel", font=sf)
            fb = pal["foot"]                                    # the darker panel at the bottom of the sidebar
            st.configure("Foot.TFrame", background=fb)
            st.configure("FootSmall.TLabel", background=fb, foreground=pal["fg"], font=sf or ("Segoe UI", 8))
            st.configure("FootMuted.TLabel", background=fb, foreground=pal["muted"], font=sf or ("Segoe UI", 8))
            st.configure("FootSection.TLabel", background=fb, foreground=pal["accent_text"], font=("Segoe UI", 9, "bold"))
            if getattr(self, "foot_line", None) is not None:
                self.foot_line.configure(bg=pal["footline"])
            st.configure("Hint.TLabel", foreground=pal["accent_text"])
            st.configure("Section.TLabel", foreground=pal["accent_text"], font=("Segoe UI", 9, "bold"))
            st.configure("RowBtn.TButton", font=("Segoe UI", 8), padding=(4, 0))
            st.configure("RowX.TButton", font=("Segoe UI", 8, "bold"), padding=(4, 0), foreground="#ef4444")
            st.map("RowX.TButton", foreground=[("disabled", pal["muted"]), ("active", "#ff6b6b"),
                                              ("!disabled", "#ef4444")])
            hf = "#06202f" if theme == "dark" else "#ffffff"           # text on the accent coloured title bar
            st.configure("RowHead.TFrame", background=pal["card"])
            st.configure("RowHeadA.TFrame", background=pal["accent"])
            st.configure("RowHead.TLabel", background=pal["card"], foreground=pal["fg"])
            st.configure("RowHeadA.TLabel", background=pal["accent"], foreground=hf)
            st.configure("RowHeadM.TLabel", background=pal["card"], foreground=pal["muted"])
            st.configure("RowHeadAM.TLabel", background=pal["accent"], foreground=hf)
            st.configure("RowCard.TFrame", background=pal["card"])
            st.configure("RowTL.TFrame", background=TL_BG)
            st.configure("RowCard.TLabel", background=pal["card"], foreground=pal["fg"])
            st.configure("RowCardMuted.TLabel", background=pal["card"], foreground=pal["muted"])
        except Exception:
            if _LOG:
                traceback.print_exc(file=_LOG)
        self._restore_tk_colors()
        for ms in (60, 300):                                # the theme recolours classic tk widgets a moment
            self.after(ms, self._restore_tk_colors)         # after it is set (<<ThemeChanged>>): put ours back
        if hasattr(self, "paned"):                          # (window not built yet during start-up)
            self._titlebar(theme == "dark")
        if save:
            self.settings["theme"] = theme
            save_settings(self.settings)

    def _icon(self, name):
        """Small button icon in the colour that suits the current theme."""
        cache = self.__dict__.setdefault("_icon_cache", {})
        key = (self.theme, name)
        if key not in cache:
            cache[key] = tk.PhotoImage(master=self, data=ICONS[self.theme][name])
        return cache[key]

    def _unpalette(self, w):
        """The theme's tk_setPalette writes its text colour into every existing ttk label / checkbox, which hides
        the style colours (blue section headers, grey hints). Clear those so the styles show again."""
        try:
            if w.winfo_class() in ("TLabel", "TCheckbutton", "TRadiobutton"):
                if str(w.cget("foreground")):
                    w.configure(foreground="")
                if str(w.cget("background")):
                    w.configure(background="")
            for c in w.winfo_children():
                self._unpalette(c)
        except Exception:
            pass

    def _restore_tk_colors(self):
        for nm, ic in (("settings", "effects"), ("presets", "presets"), ("shortcuts", "shortcuts")):
            b = getattr(self, nm + "_btn", None)
            if b is not None:
                try:
                    b.configure(image=self._icon(ic))
                except Exception:
                    pass
        if hasattr(self, "spk_btn"):
            self._spk_icon()
        if hasattr(self, "play_btn"):
            try:
                self.play_btn.configure(image=self._icon("pause" if getattr(self, "playing", False) else "play"))
            except Exception:
                pass
        self._unpalette(self)
        pal = self.pal
        TT_COLORS.update(bg=pal["card"], fg=pal["fg"], border=pal["border"])
        try:
            self.configure(bg=pal["bg"])
            for f in list(getattr(self, "_br_lines", [])) + list(getattr(self, "_br_corners", {}).values()):
                f.configure(bg="#ffffff", highlightbackground="#000000")
            for lb, col in getattr(self, "hint_lbls", []):
                if col == "#ffffff" and self.theme == "light":      # white text needs a dark box on the light look
                    lb.configure(bg="#111111", fg=col, padx=5)
                else:
                    lb.configure(bg=pal["bg"], fg=col or pal["muted"], padx=0)
            if hasattr(self, "prog"):
                self.prog.recolor()
            if isinstance(getattr(self, "clear_btn", None), RoundButton):
                self.clear_btn.recolor()
            if hasattr(self, "settings_win"):
                self.settings_win.configure(bg=pal["bg"])
            if hasattr(self, "list_canvas"):
                self.list_canvas.configure(bg=pal["bg"])
            if hasattr(self, "side_canvas"):
                self.side_canvas.configure(bg=pal["bg"])
            if hasattr(self, "add_strip"):
                self.add_strip.configure(bg=pal["bg"])
                self._draw_strip()
            if hasattr(self, "empty_lbl"):
                self.empty_lbl.configure(bg=pal["bg"])
                self._draw_empty()
            if hasattr(self, "preview_box"):
                self.preview_box.config(bg="#111")
                self.preview.config(bg="#111", fg="#888")
            for r in getattr(self, "rows", []):
                r.apply_palette()
        except Exception:
            pass

    def _toggle_theme(self):
        self.apply_theme("dark" if self.theme_var.get() else "light")

    def _fade_in(self, win):
        """Shows a window only after it is fully drawn with the right (dark) title bar, then fades it in."""
        try:
            win.attributes("-alpha", 0.0)
        except tk.TclError:
            pass
        try:
            win.deiconify()
            win.update_idletasks()
            w_, h_ = max(win.winfo_width(), win.winfo_reqwidth()), max(win.winfo_height(), win.winfo_reqheight())
            x = self.winfo_rootx() + (self.winfo_width() - w_) // 2       # centered on the main window
            y = self.winfo_rooty() + (self.winfo_height() - h_) // 2
            x = max(0, min(x, self.winfo_screenwidth() - w_ - 10))
            y = max(0, min(y, self.winfo_screenheight() - h_ - 60))
            win.geometry(f"+{x}+{y}")
            win.update_idletasks()
            self._titlebar(self.theme == "dark", win)
            win.update_idletasks()
        except tk.TclError:
            return

        def step(a):
            try:
                if not win.winfo_exists():
                    return
                if a <= 0.0:                                # last chance to finish drawing while invisible
                    win.update_idletasks()
                win.attributes("-alpha", min(1.0, a))
                if a < 1.0:
                    self.after(15, step, round(a + 0.125, 3))
            except tk.TclError:
                pass
        self.after(130, step, 0.0)                          # stay invisible until everything is drawn

    def _titlebar(self, dark, win=None):
        """Dark/light Windows title bar to match the theme (Windows 10 1809+ / 11)."""
        if sys.platform != "win32":
            return
        try:
            import ctypes
            self.update_idletasks()
            hwnd = ctypes.windll.user32.GetParent((win or self).winfo_id())
            v = ctypes.c_int(1 if dark else 0)
            for attr in (20, 19):
                if ctypes.windll.dwmapi.DwmSetWindowAttribute(hwnd, attr, ctypes.byref(v), ctypes.sizeof(v)) == 0:
                    break
        except Exception:
            pass

    # ------------------------------------------------------- drag & drop
    def _enable_file_drop(self):
        """Accept files dropped from Explorer (Windows only, no extra packages)."""
        if sys.platform != "win32":
            return
        try:
            import ctypes
            from ctypes import wintypes as wt
            WM_DROPFILES, WM_COPYGLOBALDATA = 0x0233, 0x0049
            user32, shell32, comctl32 = ctypes.windll.user32, ctypes.windll.shell32, ctypes.windll.comctl32

            SUBCLASSPROC = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, wt.HWND, wt.UINT, wt.WPARAM,
                                              wt.LPARAM, ctypes.c_size_t, ctypes.c_size_t)
            user32.GetParent.argtypes = [wt.HWND]
            user32.GetParent.restype = wt.HWND
            comctl32.SetWindowSubclass.argtypes = [wt.HWND, SUBCLASSPROC, ctypes.c_size_t, ctypes.c_size_t]
            comctl32.SetWindowSubclass.restype = wt.BOOL
            comctl32.DefSubclassProc.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
            comctl32.DefSubclassProc.restype = ctypes.c_ssize_t
            shell32.DragAcceptFiles.argtypes = [wt.HWND, wt.BOOL]
            shell32.DragQueryFileW.argtypes = [ctypes.c_void_p, wt.UINT, wt.LPWSTR, wt.UINT]
            shell32.DragQueryFileW.restype = wt.UINT
            shell32.DragFinish.argtypes = [ctypes.c_void_p]

            def on_message(hwnd, msg, wparam, lparam, uid, ref):
                if msg == WM_DROPFILES:
                    try:
                        count = shell32.DragQueryFileW(wparam, 0xFFFFFFFF, None, 0)
                        paths = []
                        for i in range(count):
                            buf = ctypes.create_unicode_buffer(2048)
                            shell32.DragQueryFileW(wparam, i, buf, 2048)
                            paths.append(buf.value)
                        shell32.DragFinish(wparam)
                        self.after(10, lambda: self.add_files(paths))
                    except Exception:
                        if _LOG:
                            traceback.print_exc(file=_LOG)
                    return 0
                return comctl32.DefSubclassProc(hwnd, msg, wparam, lparam)

            self._subclass_ref = SUBCLASSPROC(on_message)   # keep alive for the app's lifetime
            self.update_idletasks()
            hwnd = user32.GetParent(self.winfo_id()) or self.winfo_id()
            if not comctl32.SetWindowSubclass(hwnd, self._subclass_ref, 1, 0):
                raise OSError("SetWindowSubclass failed")
            shell32.DragAcceptFiles(hwnd, True)
            try:  # allow drops even if this app runs elevated
                user32.ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, 1, None)
                user32.ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, 1, None)
            except Exception:
                pass
        except Exception:
            if _LOG:
                traceback.print_exc(file=_LOG)

    # ---------------------------------------------------------------- UI
    def _build_ui(self):
        top = ttk.Frame(self, padding=(8, 8, 8, 0))
        top.pack(fill="x")
        self.proj_btn = ttk.Button(top, takefocus=False, text="Project ▾", command=self._project_menu)
        tip(self.proj_btn, "Save or open a project: the clips with their trim markers and speed sections, "
                           "plus the export settings").pack(side="left", padx=(0, 8))
        self.shortcuts_btn = ttk.Button(top, takefocus=False, text="Shortcuts", command=self.show_shortcuts,
                                        image=self._icon("shortcuts"), compound="left")
        tip(self.shortcuts_btn, "Keyboard shortcuts and tips (F1)").pack(side="left", padx=(0, 18))
        self.about_btn = ttk.Button(top, takefocus=False, text="About", command=self.show_about)
        tip(self.about_btn, "Version, changelog and licenses").pack(side="right")
        self.undo_btn = ttk.Button(top, takefocus=False, text="↶", width=3, command=self.undo, state="disabled")
        tip(self.undo_btn, "Undo the last change to the clips (Ctrl+Z)").pack(side="left", padx=(0, 2))
        self.redo_btn = ttk.Button(top, takefocus=False, text="↷", width=3, command=self.redo, state="disabled")
        tip(self.redo_btn, "Redo (Ctrl+Shift+Z)").pack(side="left", padx=(0, 6))
        self._popup, self._menu_closed = None, 0.0
        for seq in ("<Button-1>", "<Button-3>"):             # a click anywhere else closes the open drop-down menu
            self.bind_all(seq, self._popup_outside, add="+")
        self.bind_all("<Button-1>", self._release_focus, add="+")   # a click elsewhere leaves a field / drop-down
        for cls in ("TCombobox", "TEntry", "TSpinbox"):
            self.bind_class(cls, "<FocusOut>", self._clear_field_selection, add="+")
        self.bind("<FocusOut>", lambda e: self.after(120, self._popup_focus_check), add="+")
        self.bind("<Configure>", self._popup_moved, add="+")
        self.count_lbl = ttk.Label(top, text="Drag videos onto this window or click the drop zone below",
                                   style="Muted.TLabel")
        # (count_lbl is kept for the code but not shown - the export summary has the length)
        self.topbar = top
        self.bind("<F1>", lambda e: self.show_shortcuts())
        self.bind("<Key>", self._on_any_key)
        self.bind("<Control-e>", self._on_export_key)
        self.bind("<Control-E>", self._on_export_key)

        self.status = ttk.Frame(self, height=6)             # (old status bar: now only a bottom margin)
        self.status.pack(side="bottom", fill="x")
        self.out_path = None                                # (export panel: see _build_side)

        self.paned = ttk.PanedWindow(self, orient="vertical")
        self.paned.pack(fill="both", expand=True)
        for ev in ("<B1-Motion>", "<ButtonRelease-1>", "<Configure>"):
            self.paned.bind(ev, lambda e: self.after_idle(self._clamp_sash), add="+")
        mid = ttk.Frame(self.paned, padding=8)
        self.paned.add(mid, weight=0)

        left = ttk.Frame(mid)
        left.pack(side="left", fill="both", expand=True)
        self.preview_box = tk.Frame(left, bg="#111", width=PREVIEW_W, height=PREVIEW_H)
        self.preview_box.pack(fill="both", expand=True)
        self.preview_box.pack_propagate(False)
        self.preview = tk.Label(self.preview_box, bg="#111", fg="#888", text="Add a video to see the preview")
        self.preview.place(relx=0.5, rely=0.5, anchor="center")
        self.preview_box.bind("<Configure>", self._on_preview_resize)
        self._build_bracket()
        ctl = ttk.Frame(left)
        ctl.pack(side="bottom", fill="x", pady=(4, 0), before=self.preview_box)
        self.ctl_row = ctl
        self.play_btn = ttk.Button(ctl, takefocus=False, image=self._icon("play"), command=self.toggle_play)
        self.play_btn.pack(side="left")
        tip(self.play_btn, "Play / pause the active clip (Space). Starts at the yellow playhead and loops the trimmed part.")
        self.sound_var = tk.BooleanVar(value=self._saved("sound", True))
        self.pv_vol_var = tk.IntVar(value=self._saved_int("pv_vol", 100))
        PV["vol"] = max(0, min(100, self.pv_vol_var.get())) / 100.0
        self.spk_btn = ttk.Button(ctl, takefocus=False, command=lambda: self._on_mute(force=True),
                                  image=self._icon("spk" if self.sound_var.get() else "spk_off"),
                                  style=self.sty("Toolbutton") or "TButton")
        tip(self.spk_btn, "Preview sound on / off (M)").pack(side="left", padx=(10, 2))
        self.pv_vol_sc = ttk.Scale(ctl, from_=0, to=100, orient="horizontal", length=90,
                                   command=lambda v: self._pv_vol(v))
        self.pv_vol_sc.set(self.pv_vol_var.get())
        tip(self.pv_vol_sc, "Preview volume (only the preview - the export is not changed)").pack(side="left", padx=(0, 6))
        self.sound_var.trace_add("write", lambda *a: self._spk_icon())
        if self.sty("Danger.TButton"):
            self.clear_btn = ttk.Button(ctl, takefocus=False, text="Clear all", command=self.clear_all,
                                        style="Danger.TButton")
        else:                                               # no sv_ttk theme: drawn red button
            self.clear_btn = RoundButton(ctl, self, "Clear all", self.clear_all)
        tip(self.clear_btn,
            "Remove all clips from the list (can be undone)").pack(side="right")
        self.presets_btn = ttk.Button(ctl, takefocus=False, text="Bitrate presets", command=self.show_presets,
                                      image=self._icon("presets"), compound="left")
        self.settings_btn = ttk.Button(ctl, takefocus=False, text="Effects", command=self.show_settings,
                                       image=self._icon("effects"), compound="left")
        tip(self.settings_btn,
            "Clip transitions, speed up / slow motion / zoom effects and app options (F2). "
            "Changes are kept automatically.").pack(side="right", padx=(0, 10))
        tip(self.presets_btn,
            "Bitrate presets: name, Mbps and a hotkey. Pick one for each clip when exporting separate files"
            ).pack(side="right", padx=(0, 10))
        self.hint_lbls = []
        self.hint_act = {}
        for txt, col, bold, px in ((self._hint_text("speed"), "#3b82f6", True, (8, 0)),
                                   (self._hint_text("slow"), "#ec4899", True, (14, 0)),
                                   (self._hint_text("zoom"), "#ffffff", True, (14, 0)),
                                   ("Space = play / pause", None, False, (14, 0))):
            lb = tk.Label(ctl, text=txt, bd=0, padx=0, pady=0, font=("Segoe UI", 9, "bold" if bold else "normal"))
            lb.pack(side="left", padx=px)
            self.hint_lbls.append((lb, col))
            for a_ in ("speed", "slow", "zoom"):
                if txt == self._hint_text(a_):
                    self.hint_act[a_] = lb

        self.settings_win = tk.Toplevel(self)
        self.settings_win.withdraw()
        self.settings_win.title("Effects")
        self.settings_win.transient(self)
        self.settings_win.resizable(False, False)
        self.settings_win.protocol("WM_DELETE_WINDOW", self.hide_settings)
        self.settings_win.bind("<Escape>", lambda e: self.hide_settings())
        try:
            self.settings_win.configure(bg=self.pal["bg"])
        except Exception:
            pass
        outer = ttk.Frame(self.settings_win, padding=20)
        outer.pack(fill="both", expand=True)
        colL, colR = ttk.Frame(outer), ttk.Frame(outer)
        colL.grid(row=0, column=0, sticky="nw")
        for _c in (colL, colR):
            _c.columnconfigure(1, weight=1)
        opts = colL

        self.size_var = tk.StringVar(value=self._saved("size", "50"))
        self.crop_var = tk.StringVar(value=self._saved("crop", "16:9", list(ASPECTS)))
        self.res_var = tk.StringVar(value=self._saved("res", "Auto", RES_SIZE_MODE + RES_BITRATE_MODE))
        self.codec_var = tk.StringVar(value=self._saved("codec", "HEVC (hevc_nvenc)", CODECS))
        self.bitrate_var = tk.StringVar(value=self._saved("bitrate", ""))
        self._res_before_bitrate = self._saved("res_before_bitrate", "Auto", RES_SIZE_MODE)
        self.audio_var = tk.StringVar(value=self._saved("audio", "128k", ["48k", "64k", "96k", "128k", "192k"]))
        self.mode_var = tk.StringVar(value=self._saved("mode", "join", ["separate", "join"]))
        self.trans_var = tk.StringVar(value=self._saved("trans", "Gradient wipe, left to right", list(TRANSITIONS)))
        self.tlen_var = tk.StringVar(value=self._saved("tlen", "0.4"))

        r = 0

        def heading(text):
            nonlocal r
            if r:                                           # thin line between the sections
                ttk.Separator(opts, orient="horizontal").grid(row=r, column=0, columnspan=2, sticky="ew", pady=(14, 0))
                r += 1
            ttk.Label(opts, text=text, style="Section.TLabel").grid(
                row=r, column=0, columnspan=2, sticky="w", pady=(12 if r else 0, 4))
            r += 1

        self._build_side()
        heading("APP")
        tip(ttk.Checkbutton(opts, text="Restore the last session on start", variable=self.restore_var,
                            takefocus=False),
            "Reopen the clips (with trim markers and speed sections) that were open when the program was closed"
            ).grid(row=r, column=0, columnspan=2, sticky="w", pady=3)

        r += 1
        heading("CLIP TRANSITIONS")
        ttk.Label(opts, text="Transition effect:").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        self.trans_cb = ttk.Combobox(opts, textvariable=self.trans_var, width=33, state="readonly",
                                     values=list(TRANSITIONS), height=16)
        self.trans_cb.grid(row=r, column=1, sticky="w", pady=3)
        self._trans_hover_setup(self.trans_cb)
        r += 1
        ttk.Label(opts, text="Transition length (s):").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        self.tlen_sp = ttk.Spinbox(opts, from_=0.1, to=3.0, increment=0.1, width=6,
                                   textvariable=self.tlen_var, state="normal")
        self.tlen_sp.grid(row=r, column=1, sticky="w", pady=3)

        r += 1
        heading("SPEED UP EFFECTS")
        ttk.Label(opts, text="Sound:").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        ttk.Combobox(opts, textvariable=self.fast_aud_var, width=33, state="readonly",
                     values=FAST_AUDIO).grid(row=r, column=1, sticky="w", pady=3)
        r += 1
        self._volume_row(opts, r, self.fast_vol_var, "fast")
        r += 1
        ttk.Label(opts, text="Pitch:").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        ttk.Combobox(opts, textvariable=self.fast_pitch_var, width=33, state="readonly",
                     values=FAST_PITCH).grid(row=r, column=1, sticky="w", pady=3)

        r += 1
        ttk.Checkbutton(opts, text="VHS visual effect on sped-up sections", variable=self.vhs_var,
                        takefocus=False).grid(row=r, column=0, columnspan=2, sticky="w", pady=3)


        r += 1
        heading("SLOW MOTION EFFECTS")
        ttk.Label(opts, text="Sound:").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        ttk.Combobox(opts, textvariable=self.slow_aud_var, width=33, state="readonly",
                     values=SLOW_AUDIO).grid(row=r, column=1, sticky="w", pady=3)
        r += 1
        self._volume_row(opts, r, self.slow_vol_var, "slow")

        r += 1
        ttk.Label(opts, text="Pitch:").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        ttk.Combobox(opts, textvariable=self.pitch_var, width=33, state="readonly",
                     values=SLOW_PITCH).grid(row=r, column=1, sticky="w", pady=3)

        r += 1
        ttk.Label(opts, text="Smoothing:").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        ttk.Combobox(opts, textvariable=self.interp_var, width=33, state="readonly",
                     values=SLOW_INTERP).grid(row=r, column=1, sticky="w", pady=3)

        r += 1
        self.rife_btn = ttk.Button(opts, text="Download RIFE (about 12 MB)", takefocus=False, command=self.download_rife)
        self.rife_btn.grid(row=r, column=1, sticky="w", pady=3)
        r += 1
        self.rife_lbl = ttk.Label(opts, text="", style="Muted.TLabel", wraplength=330, justify="left")
        self.rife_lbl.grid(row=r, column=0, columnspan=2, sticky="w", pady=(0, 2))
        r += 1
        self.rife_pb = ttk.Progressbar(opts, orient="horizontal", mode="determinate", maximum=100, length=330)
        self.rife_pb.grid(row=r, column=0, columnspan=2, sticky="w", pady=(0, 2))
        self.rife_pb.grid_remove()
        self._rife_refresh()
        r += 1
        heading("ZOOM EFFECTS")
        ttk.Label(opts, text="Motion:").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        ttk.Combobox(opts, textvariable=self.zoom_fx_var, width=33, state="readonly",
                     values=ZOOM_FX).grid(row=r, column=1, sticky="w", pady=3)
        r += 1
        ttk.Label(opts, text="Motion length (s):").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        ttk.Spinbox(opts, from_=0.1, to=2.0, increment=0.1, width=6, textvariable=self.zoom_len_var,
                    state="normal").grid(row=r, column=1, sticky="w", pady=3)
        r += 1
        ttk.Label(opts, text="Sound:").grid(row=r, column=0, sticky="w", pady=5, padx=(0, 14))
        ttk.Combobox(opts, textvariable=self.zoom_snd_var, width=33, state="readonly",
                     values=ZOOM_AUDIO).grid(row=r, column=1, sticky="w", pady=3)
        r += 1
        self._volume_row(opts, r, self.zoom_vol_var, "zoom")

        hints = {
            "Mute microphone (2nd audio track)": "Leave out the second audio track (ShadowPlay microphone) in the preview and export",
            "Convert HDR clips to SDR (tone-map)": "Fixes dark, washed-out colours of HDR recordings",
        }
        for w in list(colL.winfo_children()) + list(colR.winfo_children()) + list(self._side_opts.winfo_children()):
            try:
                t = w.cget("text")
            except Exception:
                continue
            if t in hints:
                Tooltip(w, hints[t])

        ttk.Button(outer, text="Done", command=self.hide_settings, takefocus=False,
                   style=self.sty("Accent.TButton")).grid(row=1, column=0, columnspan=2, sticky="e", pady=(16, 0))
        for v in (self.size_var, self.bitrate_var, self.res_var, self.codec_var, self.crop_var, self.mode_var,
                  self.audio_var, self.atrk_var):
            v.trace_add("write", lambda *a: self._update_summary())
        self._update_summary()

        # scrollable list of clips
        lst = ttk.Frame(self.paned, padding=(8, 8, 8, 0))
        self.paned.add(lst, weight=1)
        self.list_canvas = tk.Canvas(lst, highlightthickness=0, height=160, bg=self.pal["bg"])
        vsb = ttk.Scrollbar(lst, orient="vertical", command=self.list_canvas.yview)
        self.list_canvas.configure(yscrollcommand=vsb.set)
        self.list_vsb = vsb
        self.list_canvas.pack(side="left", fill="both", expand=True)
        self.add_strip = tk.Canvas(lst, height=34, highlightthickness=0, bg=self.pal["bg"], cursor="hand2")
        self._strip_before, self._strip_hover = self.list_canvas, False
        self.add_strip.bind("<Configure>", lambda e: self._draw_strip())
        self.add_strip.bind("<Button-1>", lambda e: self.open_files())
        self.add_strip.bind("<Enter>", lambda e: self._set_strip_hover(True))
        self.add_strip.bind("<Leave>", lambda e: self._set_strip_hover(False))
        self.list_inner = ttk.Frame(self.list_canvas)
        self.list_win = self.list_canvas.create_window((0, 0), window=self.list_inner, anchor="nw")
        self.list_inner.bind("<Configure>", lambda e: self._fit_scroll())
        self.list_canvas.bind("<Configure>", lambda e: (self.list_canvas.itemconfigure(self.list_win, width=e.width),
                                                        self._fit_scroll()))
        self.bind_all("<MouseWheel>", self._on_wheel)
        self.empty_lbl = tk.Canvas(self.list_inner, height=180, highlightthickness=0, bg=self.pal["bg"],
                                   cursor="hand2")
        self._empty_hover = False
        self.empty_lbl.bind("<Configure>", lambda e: self._draw_empty())
        self.empty_lbl.bind("<Button-1>", lambda e: self.open_files())
        self.empty_lbl.bind("<Enter>", lambda e: self._set_empty_hover(True))
        self.empty_lbl.bind("<Leave>", lambda e: self._set_empty_hover(False))
        self.empty_lbl.pack(fill="x", padx=4, pady=6)

    def _set_strip_hover(self, flag):
        self._strip_hover = flag
        self._draw_strip()

    def _draw_strip(self):
        c, pal = self.add_strip, self.pal
        c.delete("all")
        W, H = c.winfo_width(), c.winfo_height()
        col = pal["accent"] if self._strip_hover else pal["border"]
        draw_round_box(c, 6, 4, W - 6, H - 4, 12, fill="", outline=col, width=2, dash=(6, 4))
        c.create_text(W / 2, H / 2, text="+  Add videos  -  drop files here or click to browse",
                      fill=pal["fg"] if self._strip_hover else pal["muted"], font=("Segoe UI", 9))

    def _set_empty_hover(self, flag):
        self._empty_hover = flag
        self._draw_empty()

    def _draw_empty(self):
        c, pal = self.empty_lbl, self.pal
        c.delete("all")
        W, H = c.winfo_width(), c.winfo_height()
        col = pal["accent"] if self._empty_hover else pal["border"]
        draw_round_box(c, 10, 8, W - 10, H - 8, 18, fill="", outline=col, width=2, dash=(7, 5))
        cx, cy = W / 2, H / 2 - 34
        c.create_rectangle(cx - 34, cy - 22, cx + 34, cy + 22, outline=pal["muted"], width=3)
        c.create_polygon(cx - 8, cy - 11, cx - 8, cy + 11, cx + 13, cy, fill=pal["accent"], outline="")
        c.create_text(cx, cy + 46, text="Drop videos here", fill=pal["fg"], font=("Segoe UI", 15, "bold"))
        c.create_text(cx, cy + 72, text="or click to browse  -  several clips can be trimmed, sped up and joined",
                      fill=pal["muted"], font=("Segoe UI", 10))

    # ------------------------------------------------------------ bitrate presets
    def preset_by_name(self, name):
        return next((p for p in self.presets if p["name"] == name), None)

    def preset_label(self, p):
        return "Auto bitrate" if not p else f"{p['name']} ({p['mbps']:g} Mbps)"

    def preset_labels(self):
        return ["Auto bitrate"] + [self.preset_label(p) for p in self.presets]

    def _presets_changed(self):
        save_presets(self.presets)
        for r in self.rows:
            r.refresh_presets()
        for r in self._detached:
            try:
                r.refresh_presets()
            except Exception:
                pass

    def show_presets(self):
        w = getattr(self, "presets_win", None)
        try:
            if w is not None and w.winfo_exists():
                w.deiconify()
                w.lift()
                return
        except Exception:
            pass
        w = self.presets_win = tk.Toplevel(self)
        w.withdraw()
        w.title("Bitrate presets")
        w.transient(self)
        w.resizable(False, False)
        try:
            w.configure(bg=self.pal["bg"])
        except Exception:
            pass
        w.bind("<Escape>", lambda e: w.destroy())
        self.presets_box = ttk.Frame(w, padding=16)
        self.presets_box.pack(fill="both", expand=True)
        self._presets_render()
        try:
            self.update_idletasks()
            w.update_idletasks()
            x = self.winfo_rootx() + max(20, self.winfo_width() - w.winfo_reqwidth() - 220)
            w.geometry(f"+{x}+{self.winfo_rooty() + 50}")
        except Exception:
            pass
        self._fade_in(w)

    def _presets_render(self):
        box = self.presets_box
        for c in box.winfo_children():
            c.destroy()
        ttk.Label(box, text="BITRATE PRESETS", style="Section.TLabel").grid(row=0, column=0, columnspan=5,
                                                                            sticky="w", pady=(0, 3))
        ttk.Label(box, text="Pick one for a clip in its title bar, or press its hotkey on the active clip. "
                            "Used when the clips are exported as separate files.",
                  style="Muted.TLabel", wraplength=400, justify="left").grid(row=1, column=0, columnspan=5,
                                                                              sticky="w", pady=(0, 10))
        r = 2
        for i, p in enumerate(self.presets):
            ttk.Label(box, text=p["name"], font=("Segoe UI", 9, "bold")).grid(row=r, column=0, sticky="w", pady=3)
            ttk.Label(box, text=f"{p['mbps']:g} Mbps").grid(row=r, column=1, sticky="w", padx=(18, 0))
            ttk.Label(box, text=("Key: " + p["key"].upper()) if p["key"] else "no hotkey",
                      style="Muted.TLabel").grid(row=r, column=2, sticky="w", padx=(18, 0))
            ttk.Button(box, text="Edit", takefocus=False, width=6,
                       command=lambda i=i: self._preset_edit(i)).grid(row=r, column=3, padx=(18, 4))
            x = FlatButton(box, self, "✕", lambda i=i: self._preset_delete(i), 3, "danger")
            x.grid(row=r, column=4, padx=(4, 0))
            r += 1
        if not self.presets:
            ttk.Label(box, text="No presets yet.", style="Muted.TLabel").grid(row=r, column=0, columnspan=5,
                                                                              sticky="w", pady=4)
            r += 1
        ttk.Button(box, text="+ Add profile", takefocus=False,
                   command=lambda: self._preset_edit(None)).grid(row=r, column=0, columnspan=5, sticky="w",
                                                                 pady=(12, 0))

    def _preset_delete(self, i):
        if 0 <= i < len(self.presets):
            del self.presets[i]
            self._presets_changed()
            self._presets_render()

    def _preset_edit(self, index):
        cur = self.presets[index] if index is not None else {"name": "", "mbps": 10.0, "key": ""}
        d = tk.Toplevel(self.presets_win)
        d.withdraw()
        d.title("Edit profile" if index is not None else "New profile")
        d.transient(self.presets_win)
        d.resizable(False, False)
        try:
            d.configure(bg=self.pal["bg"])
        except Exception:
            pass
        f = ttk.Frame(d, padding=16)
        f.pack()
        name_v, mbps_v, key_v = tk.StringVar(value=cur["name"]), tk.StringVar(value=f"{cur['mbps']:g}"), \
            tk.StringVar(value=cur["key"].upper())
        ttk.Label(f, text="Name:").grid(row=0, column=0, sticky="w", pady=3)
        ne = ttk.Entry(f, textvariable=name_v, width=24)
        ne.grid(row=0, column=1, sticky="w")
        ttk.Label(f, text="Bitrate (Mbps):").grid(row=1, column=0, sticky="w", pady=3)
        ttk.Entry(f, textvariable=mbps_v, width=10).grid(row=1, column=1, sticky="w")
        ttk.Label(f, text="Hotkey:").grid(row=2, column=0, sticky="w", pady=3)
        ke = ttk.Entry(f, textvariable=key_v, width=10, justify="center")
        ke.grid(row=2, column=1, sticky="w")
        ttk.Label(f, text="click the field and press a key (Backspace clears it)",
                  style="Muted.TLabel").grid(row=3, column=0, columnspan=2, sticky="w")

        def on_key(e):
            k = (e.keysym or "").lower()
            if k in ("backspace", "delete", "escape"):
                key_v.set("")
            elif (len(k) == 1 and k.isalnum()) or (k.startswith("f") and k[1:].isdigit()):
                key_v.set(k.upper())
            elif k in ("tab", "shift_l", "shift_r", "control_l", "control_r", "alt_l", "alt_r"):
                return None
            return "break"
        ke.bind("<KeyPress>", on_key)

        def ok(e=None):
            name = name_v.get().strip()
            try:
                mbps = float(mbps_v.get().replace(",", "."))
                assert 0.1 <= mbps <= 1000
            except Exception:
                messagebox.showerror("Bitrate", "Enter the bitrate in Mbps, e.g. 20.", parent=d)
                return
            key = key_v.get().strip().lower()
            if not name:
                messagebox.showerror("Name", "Give the profile a name.", parent=d)
                return
            if any(i != index and q["name"].lower() == name.lower() for i, q in enumerate(self.presets)):
                messagebox.showerror("Name", "There is already a profile with this name.", parent=d)
                return
            if key in RESERVED_KEYS:
                messagebox.showerror("Hotkey", "This key already does something else - pick another one.", parent=d)
                return
            clash = next((q for i, q in enumerate(self.presets) if i != index and key and q["key"] == key), None)
            if clash:
                messagebox.showerror("Hotkey", f"The key is already used by \"{clash['name']}\".", parent=d)
                return
            new = {"name": name, "mbps": mbps, "key": key}
            if index is None:
                self.presets.append(new)
            else:
                old = self.presets[index]["name"]
                self.presets[index] = new
                for r in self.rows:                         # renamed: clips keep their preset
                    if r.preset == old:
                        r.preset = name
            self._presets_changed()
            self._presets_render()
            d.destroy()
        bt = ttk.Frame(f)
        bt.grid(row=4, column=0, columnspan=2, sticky="e", pady=(12, 0))
        ttk.Button(bt, text="Cancel", takefocus=False, command=d.destroy).pack(side="left", padx=(0, 8))
        ttk.Button(bt, text="Save", takefocus=False, command=ok,
                   style=self.sty("Accent.TButton")).pack(side="left")
        d.bind("<Return>", ok)
        d.bind("<Escape>", lambda e: d.destroy())
        try:
            d.update_idletasks()
            d.geometry(f"+{self.presets_win.winfo_rootx() + 30}+{self.presets_win.winfo_rooty() + 40}")
            self._fade_in(d)
            d.grab_set()
            ne.focus_set()
        except Exception:
            pass

    HINT_WORDS = {"speed": "speed up", "slow": "slow down", "zoom": "zoom in"}

    def _hint_text(self, act):
        return f"{chord_label(self.keys[act])} = {self.HINT_WORDS[act]}"

    def _refresh_hints(self):
        for a_, lb in getattr(self, "hint_act", {}).items():
            lb.config(text=self._hint_text(a_))

    def key_conflict(self, ch, act):
        """Why `ch` cannot be used for action `act` (None = fine)."""
        parts = ch.split("+")
        if ch in RESERVED_CTRL:
            return f"{chord_label(ch)} is used by the program (save / open / undo ...)."
        if len(parts) == 1 and ch in RESERVED_PLAIN or ch == "shift+m":
            return f"{chord_label(ch)} is used by the program (play, mute, delete ...)."
        if any(q["key"] == ch for q in self.presets):
            return f"{chord_label(ch)} is the hotkey of a bitrate preset."
        for a_, v_ in self.keys.items():
            if v_ == ch and a_ != act:
                return f"{chord_label(ch)} is already used: {KEY_TEXT[a_].lower()}."
        return None

    def set_key(self, act, ch):
        self.keys[act] = ch
        self._refresh_hints()

    def _on_bound_key(self, e):
        ch = chord_from_event(e)
        if ch is None:
            return None
        act = next((a_ for a_, v_ in self.keys.items() if v_ == ch), None)
        if act == "effects":
            if self._typing():
                return None
            self.show_settings()
            return "break"
        if act is None or self.encoding or self._typing() or not self.rows:
            return None
        if act == "trim_start":
            return self._on_trim("start")
        if act == "trim_end":
            return self._on_trim("end")
        if act == "speed":
            return self._on_s(e, 2.0)
        if act == "slow":
            return self._on_s(e, 0.5)
        return self._on_z(e)

    def _on_any_key(self, e):
        """Hotkeys of the bitrate presets: apply the preset to the selected clips."""
        if self._on_bound_key(e):
            return "break"
        if self.encoding or self._typing() or not self.rows or (e.state & 0x4) or (e.state & 0x20000):
            return
        k = (e.keysym or "").lower()
        p = next((q for q in self.presets if q["key"] and q["key"] == k), None)
        if p is None:
            return
        sel = [r for r in self.rows if r in self.selection] or \
              [self.active_row if self.active_row in self.rows else self.rows[0]]
        if any(r.preset != p["name"] for r in sel):
            self.push_undo("bitrate preset")
        for r in sel:
            r.set_preset(p["name"])
        self.set_status(f"Bitrate preset \"{p['name']}\" ({p['mbps']:g} Mbps) applied to "
                        + (f"{len(sel)} clips" if len(sel) > 1 else os.path.basename(sel[0].path)))
        return "break"

    def show_settings(self):
        win = self.settings_win
        if win.winfo_viewable():
            self.hide_settings()
            return
        try:
            self.update_idletasks()
            win.update_idletasks()
            free = self.winfo_width() - self.SIDE_W               # centered over the clip area, not the export panel
            x = self.winfo_rootx() + max(20, (free - win.winfo_reqwidth()) // 2)
            y = self.winfo_rooty() + 50
            y = max(0, min(y, self.winfo_screenheight() - win.winfo_reqheight() - 60))
            win.geometry(f"+{x}+{y}")
        except Exception:
            pass
        self._fade_in(win)
        win.lift()

    def _rife_refresh(self):
        ready = rife_ready()
        if self.rife_busy:
            txt = self.rife_busy
        elif ready:
            txt = "RIFE is installed (used when exporting; the preview shows blending)"
        else:
            txt = "RIFE is not installed" + (" - exports use frame blending" if SLOW["rife"] else "")
        self.rife_lbl.config(text=txt)
        try:
            if self.rife_busy:
                self.rife_pb.grid()
                self.rife_pb.config(value=self.rife_pct)
            else:
                self.rife_pb.grid_remove()
        except Exception:
            pass
        if ready and not self.rife_busy:
            self.rife_btn.grid_remove()
        else:
            self.rife_btn.grid()
            self.rife_btn.config(state="disabled" if self.rife_busy else "normal")

    def download_rife(self):
        if self.rife_busy or rife_ready():
            return
        self.rife_busy = "Starting download..."
        self.rife_pct = 0
        self._rife_refresh()
        threading.Thread(target=self._rife_worker, daemon=True).start()

    def _rife_msg(self, text, pct=None):
        def f():
            self.rife_busy = text
            if pct is not None:
                self.rife_pct = pct
            self._rife_refresh()
        self.after(0, f)

    def _rife_worker(self):
        tmp = None
        msg = ""
        try:
            import urllib.request
            import zipfile
            tmp = tempfile.mkdtemp(prefix="clipshrinker_rifedl_")
            xd = os.path.join(tmp, "x")
            done_small = False
            try:                                  # only the needed files (about 20 MB instead of 430 MB)
                last = [0.0]
                planned = [0]

                def prog(got):
                    if time.time() - last[0] > 0.2 and planned[0]:
                        last[0] = time.time()
                        pct = min(99, int(100 * got / planned[0]))
                        self._rife_msg(f"Downloading RIFE: {min(got, planned[0]) / 1048576:.1f} of "
                                       f"{planned[0] / 1048576:.1f} MB ({pct}%)", pct)
                self._rife_msg("Connecting...", 0)
                rf = RangeFile(RIFE_URL, prog)
                with zipfile.ZipFile(rf) as zf:
                    keep = [m for m in zf.namelist()
                            if any(m.lower().split("/", 1)[-1].startswith(k) for k in RIFE_KEEP) and not m.endswith("/")]
                    if not any(m.lower().endswith(("rife-ncnn-vulkan.exe", "flownet.bin")) for m in keep):
                        raise OSError("unexpected zip layout")
                    planned[0] = sum(zf.getinfo(m).compress_size for m in keep) + 120000
                    for m in keep:
                        zf.extract(m, xd)
                done_small = True
            except Exception as e:
                _log(f"RIFE partial download failed ({e}); downloading the whole zip")
                shutil.rmtree(xd, ignore_errors=True)
            if not done_small:
                z = os.path.join(tmp, "rife.zip")
                req = urllib.request.Request(RIFE_URL, headers={"User-Agent": "ClipShrinker"})
                with urllib.request.urlopen(req, timeout=30) as r, open(z, "wb") as f:
                    total, got, last = int(r.headers.get("Content-Length") or 0), 0, 0.0
                    while True:
                        b = r.read(1 << 16)
                        if not b:
                            break
                        f.write(b)
                        got += len(b)
                        if time.time() - last > 0.2:
                            last = time.time()
                            pct = min(99, int(100 * got / total)) if total else 0
                            self._rife_msg(f"Downloading RIFE (full package): {got / 1048576:.0f}" +
                                           (f" of {total / 1048576:.0f}" if total else "") + f" MB ({pct}%)", pct)
                self._rife_msg("Unpacking...", 99)
                with zipfile.ZipFile(z) as zf:
                    zf.extractall(xd)
            self._rife_msg("Installing...", 100)
            exe = None
            for root, _d, files in os.walk(xd):
                for fn in files:
                    if fn.lower() in ("rife-ncnn-vulkan.exe", "rife-ncnn-vulkan"):
                        exe = os.path.join(root, fn)
            if not exe:
                raise RuntimeError("the download does not contain rife-ncnn-vulkan")
            os.makedirs(RIFE_DIR, exist_ok=True)
            shutil.copytree(os.path.dirname(exe), RIFE_DIR, dirs_exist_ok=True)
            if not rife_ready():
                raise RuntimeError("the rife-v4 model files are missing in the download")
        except Exception as e:
            msg = f"Download failed ({e}). Unzip the release from github.com/nihui/rife-ncnn-vulkan into the library\\rife folder."
        finally:
            if tmp:
                shutil.rmtree(tmp, ignore_errors=True)
        def done():
            self.rife_busy = ""
            self._rife_refresh()
            if msg:
                self.rife_lbl.config(text=msg)
                self.set_status("RIFE download failed - see Settings")
            else:
                self.set_status("RIFE installed - choose it under Effects > Slow motion effects > Smoothing")
        self.after(0, done)

    def hide_settings(self):
        self.settings_win.withdraw()
        self.focus_set()

    SIDE_W = 350

    def _build_side(self):
        """Export panel on the right: every export setting, a summary, progress and the Export button."""
        side = ttk.Frame(self, padding=0, width=self.SIDE_W)
        side.pack(side="right", fill="y", before=self.topbar)
        side.pack_propagate(False)
        ttk.Separator(self, orient="vertical").pack(side="right", fill="y", before=self.topbar)
        self.side = side

        # --- bottom part (always visible): summary, progress, buttons
        try:                                                # summary text: one size smaller than normal
            import tkinter.font as tkfont
            base = tkfont.Font(font=ttk.Style(self).lookup("TLabel", "font") or "TkDefaultFont").actual()
            small = (base["family"], max(7, abs(int(base["size"])) - 1))
        except Exception:
            small = ("Segoe UI", 8)
        self._small_font = small
        try:
            st = ttk.Style(self)
            st.configure("Small.TLabel", font=small)
            st.configure("SmallMuted.TLabel", font=small, foreground=self.pal["muted"])
            st.configure("FootSmall.TLabel", font=small)
            st.configure("FootMuted.TLabel", font=small)
        except Exception:
            pass
        body = ttk.Frame(side, padding=(16, 10, 14, 8))
        foot = ttk.Frame(side, style="Foot.TFrame", padding=(16, 0, 14, 10))
        foot.pack(side="bottom", fill="x")
        self.foot_line = tk.Frame(side, height=1, bd=0, highlightthickness=0, bg=self.pal["footline"])
        self.foot_line.pack(side="bottom", fill="x")
        body.pack(side="top", fill="both", expand=True)
        self.foot, self.side_body = foot, body
        self.go_btn = ttk.Button(foot, takefocus=False, text="Export", command=self._go_or_abort,
                                 style=self.sty("Accent.TButton"))
        self.go_btn.pack(side="bottom", fill="x", pady=(10, 0), ipady=7)
        tip(self.go_btn, "Start the export (Ctrl+E) - while exporting it stops the export")
        self.cancel_btn = self.go_btn                       # (old name)
        self.exp_msg = ttk.Label(foot, text="", style="FootMuted.TLabel", justify="left",
                                 wraplength=self.SIDE_W - 40)
        self.exp_msg.pack(side="bottom", fill="x", pady=(4, 0))
        try:                                                # the gap between the progress bar and the Export button ...
            gap = 14 + self.exp_msg.winfo_reqheight()
        except Exception:
            gap = 28
        foot.configure(padding=(16, gap, 14, 10))           # ... is also used above the summary and above the panel
        prow = ttk.Frame(foot, style="Foot.TFrame")
        prow.pack(side="bottom", fill="x", pady=(gap, 0))
        self.pct_lbl = ttk.Label(prow, text="")              # (not shown - the % is drawn in the bar)
        self.open_btn = ttk.Button(prow, takefocus=False, text="Open folder", command=self.open_output_folder)
        self.prog = StripedBar(prow, self)
        try:
            self.prog.configure(width=60)                   # grows to the free width (leaves room for Open folder)
        except Exception:
            pass
        self.prog.pack(side="left", fill="x", expand=True)
        sumf = ttk.Frame(foot, style="Foot.TFrame")
        sumf.pack(side="bottom", fill="x", pady=(0, 0))
        ttk.Label(sumf, text="SUMMARY", style="FootSection.TLabel").pack(anchor="w", pady=(0, 6))
        self.sum_lines = []
        for k in range(4):
            if k:
                ttk.Separator(sumf, orient="horizontal").pack(fill="x", pady=5)
            lb = ttk.Label(sumf, text="", style="FootSmall.TLabel", justify="left", wraplength=self.SIDE_W - 40)
            lb.pack(anchor="w", fill="x")
            self.sum_lines.append(lb)
        self.summary_lbl = self.sum_lines[0]                # (old name)

        hdr = ttk.Frame(body)
        hdr.pack(side="top", fill="x", pady=(2, 8))
        ttk.Label(hdr, text="EXPORT SETTINGS", style="Section.TLabel").pack(side="left")
        tip(ttk.Checkbutton(hdr, text="Dark mode", variable=self.theme_var, command=self._toggle_theme,
                            style=self.sty("Switch.TCheckbutton"), takefocus=False),
            "Switch between the dark and the light look").pack(side="right")

        # --- settings (scroll if the window is too low)
        wrap = ttk.Frame(body)
        wrap.pack(side="top", fill="both", expand=True)
        cv = tk.Canvas(wrap, highlightthickness=0, bd=0, bg=self.pal["bg"])
        sb = ttk.Scrollbar(wrap, orient="vertical", command=cv.yview)
        cv.configure(yscrollcommand=sb.set)
        cv.pack(side="left", fill="both", expand=True)
        opts = ttk.Frame(cv)
        win_id = cv.create_window((0, 0), window=opts, anchor="nw")
        self.side_canvas, self._side_opts = cv, opts

        busy = [False]

        def fit(*a):
            if busy[0]:                                      # (showing / hiding the scrollbar changes the width and
                return                                       # would call this again while it is still working)
            busy[0] = True
            try:
                rh, ch_ = opts.winfo_reqheight(), cv.winfo_height()
                cv.configure(scrollregion=(0, 0, opts.winfo_reqwidth(), rh))
                cv.itemconfigure(win_id, width=cv.winfo_width())
                if rh > ch_ + 2:
                    if not sb.winfo_manager():
                        sb.pack(side="right", fill="y", before=cv)
                elif rh < ch_ - 8 or not sb.winfo_manager():    # (hysteresis: no flip-flop right at the limit)
                    if sb.winfo_manager():
                        sb.pack_forget()
                    cv.yview_moveto(0)
            finally:
                busy[0] = False
        opts.bind("<Configure>", fit)
        cv.bind("<Configure>", fit)

        def wheel(e):
            if sb.winfo_manager():
                cv.yview_scroll(int(-e.delta / 120) or (-1 if e.delta > 0 else 1), "units")
                return "break"
        self._side_wheel = wheel
        opts.columnconfigure(1, weight=1)
        r = 0

        def heading(text):
            nonlocal r
            if r and SIDE_DIVIDERS:
                ttk.Separator(opts, orient="horizontal").grid(row=r, column=0, columnspan=2, sticky="ew", pady=(14, 0))
                r += 1
            ttk.Label(opts, text=text, style="Section.TLabel").grid(
                row=r, column=0, columnspan=2, sticky="w", pady=((12 if SIDE_DIVIDERS else 20) if r else 0, 6))
            r += 1

        def label(text):
            ttk.Label(opts, text=text).grid(row=r, column=0, sticky="w", pady=4, padx=(0, 12))

        label("Max size (MB):")
        f = ttk.Frame(opts)
        f.grid(row=r, column=1, sticky="w", pady=4)
        self.size_entry = ttk.Entry(f, textvariable=self.size_var, width=7)
        self.size_entry.pack(side="left")
        tip(self.size_entry, "Largest allowed file size in MB (per file, or for the whole video when joining)")
        self.size_hint = ttk.Label(f, text="per file", style="Muted.TLabel")
        self.size_hint.pack(side="left", padx=6)
        r += 1
        label("or Bitrate (Mbps):")
        f = ttk.Frame(opts)
        f.grid(row=r, column=1, sticky="w", pady=4)
        tip(ttk.Entry(f, textvariable=self.bitrate_var, width=7),
            "Fixed video bitrate in Mbps. When filled in, the size limit is ignored.").pack(side="left")
        self.bitrate_hint = ttk.Label(f, text="empty = size limit", style="Muted.TLabel")
        self.bitrate_hint.pack(side="left", padx=6)
        self.bitrate_var.trace_add("write", lambda *a: self._bitrate_changed())
        r += 1
        label("Resolution:")
        self.res_cb = ttk.Combobox(opts, textvariable=self.res_var, width=19, state="readonly", values=RES_SIZE_MODE)
        self.res_cb.grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        label("Codec:")
        ttk.Combobox(opts, textvariable=self.codec_var, width=19, state="readonly",
                     values=CODECS).grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        label("Crop (centered):")
        cb = ttk.Combobox(opts, textvariable=self.crop_var, width=19, state="readonly", values=list(ASPECTS))
        cb.grid(row=r, column=1, sticky="w", pady=4)
        cb.bind("<<ComboboxSelected>>", lambda e: (cb.selection_clear(), self.focus_set(), self._crop_changed()))
        r += 1
        self.crop_info = ttk.Label(opts, text=" ", style="SmallMuted.TLabel")      # one small line: the layout never changes size
        self.crop_info.grid(row=r, column=1, sticky="w", pady=(0, 2))
        r += 1
        ttk.Checkbutton(opts, text="Convert HDR clips to SDR (tone-map)", variable=self.hdr_var,
                        takefocus=False, command=self._hdr_changed).grid(row=r, column=0, columnspan=2,
                                                                        sticky="w", pady=4)
        r += 1
        heading("AUDIO SETTINGS")
        label("Bitrate per track:")
        ttk.Combobox(opts, textvariable=self.audio_var, width=19, state="readonly",
                     values=["48k", "64k", "96k", "128k", "192k"]).grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        label("Audio tracks:")
        ttk.Combobox(opts, textvariable=self.atrk_var, width=19, state="readonly",
                     values=["Keep separate tracks", "Mix into one track"]).grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        ttk.Checkbutton(opts, text="Mute microphone (2nd audio track)", variable=self.mutemic_var,
                        takefocus=False, command=self.stop_playback).grid(row=r, column=0, columnspan=2,
                                                                         sticky="w", pady=4)
        r += 1
        heading("FILE NAMES")
        label("Prefix:")
        tip(ttk.Entry(opts, textvariable=self.prefix_var, width=21),
            "Added before the clip name with an underscore, e.g. prefix_clip_HEVC.mp4 (empty = nothing)"
            ).grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        label("Suffix:")
        tip(ttk.Entry(opts, textvariable=self.suffix_var, width=21),
            "Added after an underscore to the exported file names, e.g. clip_suffix.mp4. "
            "Empty = the codec name (clip_HEVC.mp4)").grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        label("Save folder:")
        sf = ttk.Frame(opts)
        sf.grid(row=r, column=1, sticky="w", pady=4)
        tip(ttk.Entry(sf, textvariable=self.save_dir_var, width=15),
            "Exports are saved here without asking (empty = a save dialog asks every time)").pack(side="left")
        tip(ttk.Button(sf, text="...", width=3, takefocus=False, command=self._pick_save_dir),
            "Choose the folder").pack(side="left", padx=(6, 0))

        def bind_wheel(w):
            w.bind("<MouseWheel>", wheel, add="+")
            for c in w.winfo_children():
                if not isinstance(c, ttk.Combobox):
                    bind_wheel(c)
        bind_wheel(opts)
        cv.bind("<MouseWheel>", wheel, add="+")
        for v in (self.prefix_var, self.suffix_var, self.save_dir_var, self.codec_var, self.trans_var, self.tlen_var):
            v.trace_add("write", lambda *a: self._update_summary())

    def _go_or_abort(self):
        if self.encoding:
            self.cancel()
        else:
            self.start_export()

    def _go_state(self, busy):
        try:
            if busy:
                self.go_btn.config(text="Abort", style=self.sty("Danger.TButton") or self.sty("Accent.TButton"))
            else:
                self.go_btn.config(text="Export", style=self.sty("Accent.TButton"))
        except tk.TclError:
            pass

    def set_export_msg(self, text):
        """Small line under the progress bar (export steps and results)."""
        self.after(0, lambda: self.exp_msg.config(text=text))

    # ---------------------------------------------- short messages over the bottom of the preview
    def _toast(self, text):
        lb = getattr(self, "toast_lbl", None)
        if lb is None:
            lb = self.toast_lbl = tk.Label(self.preview_box, bd=0, padx=14, pady=6,
                                           font=("Segoe UI", 10), wraplength=700, justify="center")
        for j in getattr(self, "_toast_jobs", []):
            try:
                self.after_cancel(j)
            except Exception:
                pass
        fg0, bg0, bgv = (255, 255, 255), (0, 0, 0), (17, 17, 17)     # preview background = #111
        col = lambda c: "#%02x%02x%02x" % c
        lb.config(text=text, fg=col(fg0), bg=col(bg0))
        lb.place(relx=0.5, rely=1.0, y=-14, anchor="s")
        lb.lift()

        def fade(k):
            if k >= 8:
                lb.place_forget()
                return
            t = (k + 1) / 8
            mix = lambda a, b: tuple(int(x + (y - x) * t) for x, y in zip(a, b))
            lb.config(fg=col(mix(fg0, bgv)), bg=col(mix(bg0, bgv)))
            self._toast_jobs.append(self.after(45, fade, k + 1))
        self._toast_jobs = [self.after(3000, fade, 0)]

    def _out_stem(self, stem):
        """clip -> prefix_clip_suffix (the file name without extension)."""
        pre = clean_suffix(self.prefix_var.get())
        return (f"{pre}_" if pre else "") + f"{stem}_{self._name_tag()}"

    def _update_summary(self):
        try:
            rows = list(getattr(self, "rows", []))
            try:
                total = sum(pieces_duration(get_pieces(r.clip())) for r in rows)
            except Exception:
                total = sum(r.end - r.start for r in rows)
            br = self._bitrate() if self.bitrate_var.get().strip() else None
            codec = codec_short(self.codec_var.get())
            crop = self.crop_var.get().split(" ")[0]
            n = len(rows)
            joined = n > 1 and TRANSITIONS.get(self.trans_var.get())
            if joined:                                      # transitions overlap the clips
                try:
                    T = max(0.1, min(3.0, float(self.tlen_var.get().replace(",", "."))))
                except ValueError:
                    T = 0.4
                total -= (n - 1) * T
            l1 = (f"{n} clip{'s' if n != 1 else ''}  ·  {fmt_time(total)}" + (" when joined" if joined else "")
                  if rows else "No clips yet")
            l2 = f"{codec}  ·  {self.res_var.get()} resolution  ·  " + ("no crop" if crop == "Original" else f"crop {crop}")
            if br:
                ak = int(self.audio_var.get().rstrip("k") or 128)
                mb = max(total, 0) * (br * 1e6 + ak * 1000) / 8 / 1048576
                l3 = f"Fixed {br:g} Mbps video" + (f"  ·  about {mb:.0f} MB in total" if rows else "")
            else:
                l3 = f"Max {self.size_var.get().strip() or '?'} MB per file"
            stem = os.path.splitext(os.path.basename(rows[0].path))[0] if rows else "clip"
            l4 = f"{self._out_stem(stem)}.mp4"
            for lb, t in zip(self.sum_lines, (l1, l2, l3, l4)):
                lb.config(text=t)
        except Exception:
            pass

    @staticmethod
    def _read_text(*paths):
        for p_ in paths:
            try:
                with open(p_, "r", encoding="utf-8", errors="replace") as f:
                    return f.read().strip()
            except OSError:
                continue
        return None

    def _already_open(self, attr):
        """One copy of a window at a time: bring the open one to the front."""
        w = getattr(self, attr, None)
        try:
            if w is not None and w.winfo_exists():
                w.deiconify()
                w.lift()
                w.focus_force()
                return True
        except tk.TclError:
            pass
        return False

    def show_about(self):
        """Version, changelog and licenses (read from the text files next to the program)."""
        if self._already_open("_about_win"):
            return
        win = self._about_win = tk.Toplevel(self)
        win.withdraw()
        win.title(f"About {APP_NAME}")
        win.transient(self)
        win.resizable(True, True)
        win.minsize(560, 420)
        box = ttk.Frame(win, padding=(24, 20))
        box.pack(fill="both", expand=True)
        ttk.Label(box, text=f"{APP_NAME}  v{APP_VERSION}", font=("Segoe UI", 16, "bold")).pack(anchor="w")
        ttk.Label(box, text="Made with Claude (Anthropic) by Vergil. Free and open source (MIT license).",
                  style="Muted.TLabel").pack(anchor="w", pady=(4, 12))
        ttk.Separator(box, orient="horizontal").pack(fill="x", pady=(0, 12))
        root = os.path.dirname(APP_DIR)                      # (library\.. = the folder with the txt files)
        lib = APP_DIR
        pal = self.pal
        tabs = ttk.Notebook(box)
        tabs.pack(fill="both", expand=True)

        def add_tab(title, text):
            f = ttk.Frame(tabs)
            tabs.add(f, text=title)
            sb = ttk.Scrollbar(f, orient="vertical")
            tx = tk.Text(f, width=105, height=30, wrap="word", bg=pal["card"], fg=pal["fg"], relief="flat",
                         borderwidth=0, padx=12, pady=10, font=("Consolas", 9), yscrollcommand=sb.set,
                         highlightthickness=0)
            sb.config(command=tx.yview)
            sb.pack(side="right", fill="y")
            tx.pack(side="left", fill="both", expand=True)
            tx.insert("1.0", text)
            tx.config(state="disabled")

        add_tab("Changelog", self._read_text(os.path.join(root, "CHANGELOG.txt")) or "CHANGELOG.txt was not found.")
        parts = []
        for title, paths in (
                (f"{APP_NAME}", [os.path.join(root, "LICENSE.txt")]),
                ("ffmpeg / ffprobe", [os.path.join(root, "ffmpeg-LICENSE.txt")]),
                ("sv_ttk (Sun Valley theme)", [os.path.join(lib, "sv_ttk", "LICENSE.txt")]),
                ("RIFE ncnn Vulkan", [os.path.join(lib, "rife", "LICENSE.txt")]),
                ("Python", [os.path.join(lib, "python", "LICENSE.txt")])):
            txt = self._read_text(*paths)
            if txt:
                parts.append(f"{'=' * 70}\n{title}\n{'=' * 70}\n\n{txt}")
        add_tab("Licenses", "\n\n\n".join(parts) or "No license files were found.")
        add_tab("Readme", self._read_text(os.path.join(root, "README.txt")) or "README.txt was not found.")
        bf = ttk.Frame(box)
        bf.pack(side="bottom", fill="x", pady=(14, 0), before=tabs)
        ttk.Button(bf, text="Close", command=win.destroy).pack(side="right")
        win.bind("<Escape>", lambda e: win.destroy())
        try:
            win.configure(bg=self.pal["bg"])
        except Exception:
            pass
        self._fade_in(win)

    def show_shortcuts(self):
        if self._already_open("_shortcuts_win"):
            return
        win = self._shortcuts_win = tk.Toplevel(self)
        win.withdraw()
        win.title("Shortcuts")
        win.transient(self)
        win.resizable(False, False)
        box = ttk.Frame(win, padding=(26, 20))
        box.pack(fill="both", expand=True)
        sections = [
            ("GENERAL", [
                ("Show this window", "F1"),
                (KEY_TEXT["effects"], ("bind", "effects")),
                ("Start the export", "Ctrl + E"),
                ("Open a project", "Ctrl + O"),
                ("Save the project", "Ctrl + S"),
                ("Save the project under a new name", "Ctrl + Shift + S"),
                ("Undo / redo", "Ctrl + Z / Ctrl + Shift + Z"),
            ]),
            ("PREVIEW", [
                ("Play / pause (starts at the playhead, loops the trimmed part)", "Space"),
                ("Mute / unmute the preview sound", "M"),
            ]),
            ("TRIMMING", [
                (KEY_TEXT["trim_start"], ("bind", "trim_start")),
                (KEY_TEXT["trim_end"], ("bind", "trim_end")),
                ("Trim by dragging the handles", "Green / red handle"),
                ("Zoom the timeline at the mouse pointer", "Ctrl + mouse wheel"),
                ("Scroll a zoomed timeline", "Mouse wheel"),
            ]),
            ("SPEED, SLOW MOTION & ZOOM", [
                (KEY_TEXT["speed"], ("bind", "speed")),
                (KEY_TEXT["slow"], ("bind", "slow")),
                (KEY_TEXT["zoom"], ("bind", "zoom")),
                ("Choose the zoomed area: move / resize the bracket on the preview", "Drag it"),
                ("Resize or move a section", "Drag its edges / bar"),
                ("Remove the selected section", "Delete or right-click"),
            ]),
            ("CLIPS", [
                ("Add videos", "Drop them onto the window"),
                ("Select a clip / a range / add or remove one", "Click / Shift + click / Ctrl + click"),
                ("Select all clips", "Ctrl + A"),
                ("Remove the selected clips", "Delete"),
                ("Change the order (= order of a joined video)", "Drag the title line"),
                ("Apply a bitrate preset to the selected clips", "The preset's own key"),
            ]),
        ]
        cols = [ttk.Frame(box), ttk.Frame(box)]            # two columns: the window stays low enough for a 1080p screen
        cols[0].grid(row=0, column=0, sticky="n")
        ttk.Separator(box, orient="vertical").grid(row=0, column=1, sticky="ns", padx=28)
        cols[1].grid(row=0, column=2, sticky="n")
        for colf, group in ((cols[0], sections[:3]), (cols[1], sections[3:])):
            r = 0
            for title, items in group:
                if r:                                       # thin line between the sections
                    ttk.Separator(colf, orient="horizontal").grid(row=r, column=0, columnspan=2, sticky="ew",
                                                                  pady=(14, 0))
                    r += 1
                ttk.Label(colf, text=title, style="Section.TLabel").grid(row=r, column=0, columnspan=2, sticky="w",
                                                                         pady=(0 if r == 0 else 12, 6))
                r += 1
                for desc, key in items:
                    ttk.Label(colf, text=desc, wraplength=330, justify="left").grid(row=r, column=0, sticky="w",
                                                                                  padx=(0, 32), pady=3)
                    if isinstance(key, tuple):
                        self._bind_row(win, colf, r, key[1])
                    else:
                        ttk.Label(colf, text=key, font=("Segoe UI", 9, "bold")).grid(row=r, column=1, sticky="e", pady=3)
                    r += 1
        ttk.Label(box, text="Click a key to change it (any key, also with Shift / Ctrl / Alt). "
                            "\"Default\" restores the original.", style="Muted.TLabel").grid(
            row=1, column=0, columnspan=2, sticky="w", pady=(18, 0))
        ttk.Button(box, text="Close", command=win.destroy).grid(row=1, column=2, sticky="e", pady=(18, 0))
        win.bind("<Escape>", lambda e: win.destroy())
        try:
            win.configure(bg=self.pal["bg"])
            self.update_idletasks()
            win.geometry(f"+{self.winfo_rootx() + 120}+{self.winfo_rooty() + 90}")
        except Exception:
            pass
        self._fade_in(win)

    def _bind_row(self, win, box, r, act):
        """One rebindable shortcut in the Shortcuts window: [key button] [Default]."""
        f = ttk.Frame(box)
        f.grid(row=r, column=1, sticky="e", pady=2)
        kb = ttk.Button(f, width=14, takefocus=False)
        kb.pack(side="left")
        db = ttk.Button(f, text="Default", width=8, takefocus=False)
        db.pack(side="left", padx=(8, 0))
        note = {"after": None}

        def refresh():
            kb.config(text=chord_label(self.keys[act]))
            db.config(state="normal" if self.keys[act] != KEY_DEFAULTS[act] else "disabled")

        def stop():
            win.bind("<Key>", lambda e: None)
            win.bind("<Escape>", lambda e: win.destroy())
            refresh()

        def got(e):
            ch = chord_from_event(e)
            if ch is None:
                return "break"                                  # only a modifier so far: wait for the real key
            if ch == "escape":
                stop()
                return "break"
            why = self.key_conflict(ch, act)
            if why:
                kb.config(text="Not possible")
                self.set_status(why + " Choose another key.")
                win.after(1200, refresh)
                stop_binding()
                return "break"
            self.set_key(act, ch)
            self.set_status(f"{KEY_TEXT[act]}: {chord_label(ch)}")
            stop()
            return "break"

        def stop_binding():
            win.bind("<Key>", lambda e: None)
            win.bind("<Escape>", lambda e: win.destroy())

        def start():
            kb.config(text="Press a key...")
            win.bind("<Key>", got)
            win.bind("<Escape>", lambda e: (stop(), "break")[1])
            win.focus_force()

        def reset():
            clash = next((a_ for a_, v_ in self.keys.items() if v_ == KEY_DEFAULTS[act] and a_ != act), None)
            if clash is not None:                              # the default key was taken by another action: swap back
                self.keys[clash] = KEY_DEFAULTS[clash]
            self.set_key(act, KEY_DEFAULTS[act])
            for w_ in getattr(win, "_bind_refreshers", []):
                w_()

        kb.config(command=start)
        db.config(command=reset)
        if not hasattr(win, "_bind_refreshers"):
            win._bind_refreshers = []
        win._bind_refreshers.append(refresh)
        refresh()

    def _pick_save_dir(self):
        d = filedialog.askdirectory(title="Default save folder", initialdir=self._init_dir(os.path.expanduser("~")), parent=self.settings_win)
        if d:
            self.save_dir_var.set(d)

    def _init_dir(self, fallback):
        d = (self.save_dir_var.get() or "").strip()
        return d if d and os.path.isdir(d) else fallback

    def _auto_path(self, folder, stem, taken=()):
        """folder/stem_tag.mp4, made unique (name (2).mp4 ...) so nothing is overwritten."""
        base = os.path.join(folder, self._out_stem(stem))
        p, i = base + ".mp4", 1
        while os.path.exists(p) or p.lower() in taken:
            i += 1
            p = f"{base} ({i}).mp4"
        return p

    def _name_tag(self):
        return clean_suffix(self.suffix_var.get()) or ("HEVC" if "HEVC" in self.codec_var.get() else "H264")

    def play_effect_sample(self, kind, btn=None):
        """Plays a short sample of the sped-up / slow-mo / zoom sound with the current settings."""
        self.stop_playback()
        row = self.active_row or (self.rows[0] if self.rows else None)
        path, t0 = None, 0.0
        if row is not None and row.info.get("audio"):
            need = {"fast": 5.8, "slow": 3.2}.get(kind, 3.4)
            path = row.path
            t0 = row.playhead if row.start <= row.playhead < row.end else row.start
            t0 = max(0.0, min(t0, row.info["dur"] - need))
        self._sample_gen = getattr(self, "_sample_gen", 0) + 1
        gen = self._sample_gen
        if btn is not None:
            btn.config(text="...")
        out = os.path.join(tempfile.gettempdir(), f"clipshrinker_{os.getpid()}_sample{gen}.wav")

        def work():
            ok = False
            try:
                r = run_capture(effect_sample_cmd(kind, path, t0, out))
                ok = r.returncode == 0 and os.path.exists(out)
                if ok:
                    _TEMP_WAVS.append(out)
                else:
                    _log(f"effect sample failed: {r.stderr[-400:] if r.stderr else ''}")
            except Exception as e:
                _log(f"effect sample failed: {e}")

            def done():
                if btn is not None:
                    try:
                        btn.config(text="▶")
                    except tk.TclError:
                        pass
                if not ok or gen != self._sample_gen:
                    if not ok:
                        self.set_status("Could not make the sound sample")
                    return
                if winsound is not None:
                    try:
                        winsound.PlaySound(out, winsound.SND_FILENAME | winsound.SND_ASYNC)
                    except Exception:
                        pass
                self.set_status("Playing a sample: normal sound, then the effect, then normal again"
                                + ("" if path else " (no clip with sound loaded - a test melody is used)"))
            self.after(0, done)
        threading.Thread(target=work, daemon=True).start()

    VOL_DEFAULT = {"fast": 70, "slow": 150, "zoom": 80}

    def _volume_row(self, parent, row, var, kind=None):
        ttk.Label(parent, text="Volume:").grid(row=row, column=0, sticky="w", pady=5, padx=(0, 14))
        f = ttk.Frame(parent)
        f.grid(row=row, column=1, sticky="w", pady=5)
        lbl = ttk.Label(f, text=f"{var.get()}%", width=5, anchor="e")

        def moved(v):
            n = int(round(float(v) / 5.0) * 5)
            if n != var.get():
                var.set(n)
        sc = ttk.Scale(f, from_=0, to=200, orient="horizontal", length=190, command=moved)
        sc.set(var.get())
        sc.pack(side="left")
        lbl.pack(side="left", padx=(8, 0))
        if kind:
            pb = ttk.Button(f, text="▶", width=3, takefocus=False)
            pb.config(command=lambda: self.play_effect_sample(kind, pb))
            pb.pack(side="left", padx=(8, 0))
            Tooltip(pb, "Play a short sample with the current settings: normal sound, then the effect, then normal "
                        "again (uses the selected clip's sound at the playhead)")
        var.trace_add("write", lambda *a: (lbl.config(text=f"{var.get()}%"),
                                           abs(sc.get() - var.get()) > 2.5 and sc.set(var.get())))
        dflt = self.VOL_DEFAULT.get(kind, 100)
        Tooltip(sc, f"Loudness of this sound in the sections (100% = unchanged, double-click = default {dflt}%)")
        sc.bind("<Double-Button-1>", lambda e: var.set(dflt))

    def _fit_scroll(self):
        """Scroll region is never smaller than the view, so no empty space can be scrolled into view."""
        try:
            c = self.list_canvas
            h = self.list_inner.winfo_reqheight()
            ch = c.winfo_height()
            c.configure(scrollregion=(0, 0, max(1, c.winfo_width()), max(h, ch)))
            need = h > ch + 3
            if need and not self.list_vsb.winfo_manager():
                self.list_vsb.pack(side="right", fill="y", before=c)
            elif not need and self.list_vsb.winfo_manager():
                self.list_vsb.pack_forget()
            if not need:
                c.yview_moveto(0)
        except Exception:
            pass

    def _on_wheel(self, e):
        w = self.winfo_containing(e.x_root, e.y_root)
        if w is not None and str(w).startswith(str(self.list_canvas)):
            self.list_canvas.yview_scroll(-int(e.delta / 120), "units")

    def _bitrate(self):
        """Fixed bitrate in Mbps from the entry, or None when it is empty / not a positive number."""
        txt = self.bitrate_var.get().strip().replace(",", ".")
        try:
            v = float(txt)
        except ValueError:
            return None
        return v if v > 0 else None

    def _bitrate_changed(self):
        if self._bitrate() is not None:                     # bitrate mode: size limit is ignored
            self.size_entry.config(state="disabled")
            self.size_hint.config(text="ignored")
            if self.res_var.get() not in RES_BITRATE_MODE:
                self._res_before_bitrate = self.res_var.get()
                self.res_var.set("Original")
            self.res_cb.config(values=RES_BITRATE_MODE)
        else:
            self.size_entry.config(state="normal")
            self.size_hint.config(text="per file")
            self.res_cb.config(values=RES_SIZE_MODE)
            if self.res_var.get() not in RES_SIZE_MODE:
                self.res_var.set(self._res_before_bitrate if self._res_before_bitrate in RES_SIZE_MODE else "Auto")

    def _check_tools(self):
        if not FFMPEG or not FFPROBE:
            messagebox.showerror("ffmpeg missing",
                                 "Could not find ffmpeg.exe / ffprobe.exe.\n\n"
                                 "Put both files in the same folder as this program "
                                 "or add them to PATH.")
            return
        r = run_capture([FFMPEG, "-hide_banner", "-encoders"])
        if "h264_nvenc" not in (r.stdout or ""):
            messagebox.showwarning("NVENC not available",
                                   "This ffmpeg build has no NVENC encoder. "
                                   "Update your NVIDIA driver or use a full ffmpeg build.")

    def set_status(self, text):
        """Editing feedback: shown for ~3 s over the bottom of the preview, then fades out."""
        self.after(0, lambda: self._toast(text))

    def set_progress(self, pct):
        self.after(0, lambda: (self.prog.config(value=pct),
                               None))

    def _mode_changed(self):
        self.size_hint.config(text="ignored" if self._bitrate() else "per file")

    def _ratio(self):
        return ASPECTS.get(self.crop_var.get())

    def _crop_changed(self):
        self._update_crop_info()
        if self.playing:
            self.start_playback()
        elif self._last_preview:
            row, t = self._last_preview
            if row in self.rows:
                self.request_preview(row, t)

    def _hdr_changed(self):
        for r in self.rows:
            if r.info["trc"]:
                r.start_thumbs()
        self._crop_changed()                     # refreshes / restarts the preview

    def _update_crop_info(self):
        if not self.rows or not self._ratio():
            txt = " "
        else:
            i = self.rows[0].info
            cw, ch = crop_size(i["w"], i["h"], self._ratio())
            txt = (f"{i['w']}x{i['h']} -> {cw}x{ch}" if (cw, ch) != (i["w"], i["h"])
                   else "(already narrower - no crop)")
        if txt != self.crop_info.cget("text"):                # (an unchanged text must not touch the layout)
            self.crop_info.config(text=txt)

    # ----------------------------------------------------------- clip list
    def open_files(self):
        if self.encoding:
            return
        ps = filedialog.askopenfilenames(title="Add videos",
                                         filetypes=[("Video files", "*.mp4 *.mov *.mkv *.m4v *.avi *.webm"),
                                                    ("All files", "*.*")])
        if ps:
            self.add_files(list(ps))

    def add_files(self, paths):
        if self.encoding:
            self.set_status("Wait for the current export to finish before adding clips.")
            return
        projects = [p for p in paths if p.lower().endswith(PROJECT_EXT)]
        if projects:
            self.open_project(projects[0])
            return []
        failed, new_rows = [], []
        added = 0
        pushed = self.push_undo("add clips")
        serial = self._undo_serial
        for p in paths:
            if not os.path.isfile(p) or not p.lower().endswith(VIDEO_EXTS):
                failed.append(f"{os.path.basename(p)}: not a video file")
                continue
            try:
                info = probe(p)
            except Exception as e:
                failed.append(f"{os.path.basename(p)}: {str(e)[:120]}")
                continue
            self.empty_lbl.pack_forget()
            row = ClipRow(self.list_inner, self, p, info)
            row.pack(fill="x", pady=(0, 8), padx=2)
            self.rows.append(row)
            new_rows.append(row)
            self.update_idletasks()
            row.start_thumbs()
            added += 1
            if added == 1:
                self.request_preview(row, 0.0)
        if pushed and not added and self._undo_serial == serial and self.undo_stack:
            self.undo_stack.pop()                           # nothing was added: no undo step
            self._update_undo_btns()
        self._renumber()
        if failed:
            messagebox.showwarning("Some files were skipped", "\n".join(failed))
        return new_rows

    # ------------------------------------------------------------ projects
    def _popup_outside(self, e):
        pop = self._popup
        if pop is None:
            return
        try:
            if str(e.widget).startswith(str(pop)):
                return
        except Exception:
            pass
        pop.close()

    _INPUT_CLASSES = ("TEntry", "Entry", "TSpinbox", "Spinbox", "TCombobox", "Text")

    def _release_focus(self, e):
        """Clicking on something that is not an input field takes the focus away from the field / drop-down
        that had it (clicks on empty areas, labels and timelines do not move the keyboard focus by themselves)."""
        try:
            w = e.widget
            if w.winfo_toplevel() is not self or "popdown" in str(w):
                return
            if w.winfo_class() in self._INPUT_CLASSES:
                return
            f = self.focus_get()
            if f is not None and f is not self and f.winfo_class() in self._INPUT_CLASSES:
                self.after_idle(self.focus_set)         # the field's own FocusOut commits typed values
        except Exception:
            pass

    @staticmethod
    def _clear_field_selection(e):
        try:
            e.widget.selection_clear()
        except Exception:
            pass

    def _popup_moved(self, e):
        """The window was moved / resized (also by dragging its title bar): the menu would stay behind."""
        if self._popup is not None and e.widget is self:
            self._popup.close()

    def _popup_watch(self):
        """Safety net while a menu is open: close it when the window moved or the program lost the focus."""
        pop = self._popup
        if pop is None:
            return
        try:
            pos = (self.winfo_rootx(), self.winfo_rooty(), self.winfo_width(), self.winfo_height())
            if getattr(pop, "_pos", pos) != pos or self.state() == "iconic":
                pop.close()
                return
        except Exception:
            return
        self.after(100, self._popup_watch)

    def _popup_focus_check(self):
        if self._popup is not None:
            try:
                if self.focus_displayof() is None:           # the program lost the focus (another window was clicked)
                    self._popup.close()
            except Exception:
                pass

    def _project_menu(self):
        if self._popup is not None:
            self._popup.close()
            return
        if time.time() - self._menu_closed < 0.5:           # the click that closed it must not reopen it
            return
        b = self.proj_btn
        self._popup = PopupMenu(self, [("Open project", "Ctrl+O", self.open_project),
                                       ("Save project", "Ctrl+S", self.save_project),
                                       ("Save project as", "Ctrl+Shift+S", lambda: self.save_project(True))])
        self._popup.show(b.winfo_rootx(), b.winfo_rooty() + b.winfo_height() + 4)
        self._popup._pos = (self.winfo_rootx(), self.winfo_rooty(), self.winfo_width(), self.winfo_height())
        self.after(100, self._popup_watch)

    def project_data(self):
        base = os.path.dirname(self.project_path) if self.project_path else None
        clips = []
        for r in self.rows:
            c = {"path": r.path, "start": round(r.start, 3), "end": round(r.end, 3), "preset": r.preset,
                 "sections": [{"a": round(x["a"], 3), "b": round(x["b"], 3), "speed": x["speed"]}
                              for x in sorted(r.sections, key=lambda x: x["a"])],
                 "zooms": [{"a": round(z["a"], 3), "b": round(z["b"], 3), "x": round(z["x"], 4),
                            "y": round(z["y"], 4), "w": round(z["w"], 4)}
                           for z in sorted(r.zooms, key=lambda z: z["a"])]}
            if base:
                try:
                    c["rel"] = os.path.relpath(r.path, base)
                except ValueError:
                    pass
            clips.append(c)
        return {"app": "Clip Shrinker", "format": 1, "clips": clips,
                "active": self.rows.index(self.active_row) if self.active_row in self.rows else 0,
                "settings": {"size": self.size_var.get(), "bitrate": self.bitrate_var.get(),
                             "res": self.res_var.get(), "codec": self.codec_var.get(), "crop": self.crop_var.get(),
                             "audio": self.audio_var.get(), "atrk": self.atrk_var.get(),
                             "mute_mic": bool(self.mutemic_var.get()),
                             "fast_audio": self.fast_aud_var.get(), "slow_audio": self.slow_aud_var.get(), "slow_pitch": self.pitch_var.get(), "fast_pitch": self.fast_pitch_var.get(),
                             "vhs": bool(self.vhs_var.get()),
                             "fast_vol": int(self.fast_vol_var.get()), "slow_vol": int(self.slow_vol_var.get()),
                             "zoom_fx": self.zoom_fx_var.get(), "zoom_snd": self.zoom_snd_var.get(), "zoom_len": self.zoom_len_var.get(), "zoom_vol": int(self.zoom_vol_var.get()),
                             "slow_interp": self.interp_var.get(), "hdr": bool(self.hdr_var.get()), "mode": self.mode_var.get(),
                             "trans": self.trans_var.get(), "tlen": self.tlen_var.get()}}

    def save_project(self, save_as=False):
        if not self.rows:
            self.set_status("Nothing to save - add some clips first")
            return False
        path = self.project_path
        if save_as or not path:
            init = os.path.dirname(self.rows[0].path)
            path = filedialog.asksaveasfilename(
                title="Save project", defaultextension=PROJECT_EXT, initialdir=init,
                initialfile=os.path.basename(self.project_path) if self.project_path else
                os.path.splitext(os.path.basename(self.rows[0].path))[0] + PROJECT_EXT,
                filetypes=[("Clip Shrinker project", "*" + PROJECT_EXT), ("All files", "*.*")])
            if not path:
                return False
        self.project_path = path
        try:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(self.project_data(), f, indent=1)
        except OSError as e:
            messagebox.showerror("Could not save the project", str(e))
            return False
        self._update_title()
        self.set_status(f"Project saved: {path}")
        return True

    def open_project(self, path=None):
        if self.encoding:
            return
        if path is None:
            path = filedialog.askopenfilename(
                title="Open project",
                filetypes=[("Clip Shrinker project", "*" + PROJECT_EXT), ("All files", "*.*")])
            if not path:
                return
        if self.rows and not messagebox.askyesno("Open project", "Replace the clips that are open now?"):
            return
        self.load_project(path)

    def load_project(self, path, quiet=False):
        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
            assert isinstance(data, dict) and isinstance(data.get("clips"), list)
        except Exception:
            if not quiet:
                messagebox.showerror("Could not open the project", "This is not a valid Clip Shrinker project file.")
            return False
        self.stop_playback()
        if self.rows:
            self.push_undo("open project")
        for r in list(self.rows):
            self._detach_row(r)
        self.rows.clear()
        self.selection = set()
        self.active_row = None
        self._last_preview = None
        self.preview.config(image="", text="Add a video to see the preview")
        self._apply_project_settings(data.get("settings"))
        base = os.path.dirname(os.path.abspath(path))
        missing, picked = [], []
        for c in data["clips"]:
            p = c.get("path", "") if isinstance(c, dict) else ""
            if not os.path.isfile(p) and isinstance(c, dict) and c.get("rel"):
                alt = os.path.normpath(os.path.join(base, c["rel"]))
                p = alt if os.path.isfile(alt) else p
            if not os.path.isfile(p):
                missing.append(os.path.basename(str(c.get("path", "?")) if isinstance(c, dict) else "?"))
                continue
            picked.append((p, c))
        self._undo_off = True
        try:
            self._load_clips(picked)
        finally:
            self._undo_off = False
        if self.rows:
            idx = data.get("active", 0)
            self.set_active(self.rows[idx if isinstance(idx, int) and 0 <= idx < len(self.rows) else 0])
            self.request_preview(self.active_row, self.active_row.start)
        self._renumber()
        self._update_crop_info()
        self._update_summary()
        self.project_path = path if not quiet else None
        self._update_title()
        if missing:
            msg = "These files were not found and were skipped:\n\n" + "\n".join(missing[:15])
            if quiet:
                self.set_status(f"{len(missing)} clip(s) of the last session were not found")
            else:
                messagebox.showwarning("Some clips are missing", msg)
        else:
            self.set_status(f"Opened {os.path.basename(path)}" if not quiet else "Last session restored")
        return True

    def _load_clips(self, picked):
        for p, c in picked:
            rows = self.add_files([p])
            if not rows:
                continue
            row = rows[0]
            dur = row.info["dur"]
            try:
                a, b = float(c.get("start", 0)), float(c.get("end", dur))
                if 0 <= a < b <= dur + 0.01:
                    row.start, row.end = a, min(b, dur)
                row.playhead = row.start
                if isinstance(c.get("preset"), str):
                    row.set_preset(c["preset"])
                for sd in c.get("sections", []):
                    sa, sb, sp = float(sd["a"]), float(sd["b"]), clamp_speed(float(sd["speed"]))
                    if 0 <= sa < sb <= dur + 0.01:
                        row.sections.append(row.new_section(sa, min(sb, dur), sp))
                for zd in c.get("zooms", []):
                    za, zb, zw = float(zd["a"]), float(zd["b"]), float(zd["w"])
                    zw = min(1.0, max(ZOOM_MIN_W, zw))
                    zx, zy = min(max(float(zd["x"]), 0.0), 1 - zw), min(max(float(zd["y"]), 0.0), 1 - zw)
                    if 0 <= za < zb <= dur + 0.01:
                        row.zooms.append({"a": za, "b": min(zb, dur), "x": zx, "y": zy, "w": zw})
            except (TypeError, ValueError, KeyError):
                pass
            row.redraw()

    def _apply_project_settings(self, st):
        if not isinstance(st, dict):
            return
        def put(var, key, allowed=None):
            v = st.get(key)
            if isinstance(v, str) and (allowed is None or v in allowed):
                var.set(v)
        put(self.size_var, "size")
        put(self.bitrate_var, "bitrate")                    # first: switches the resolution list
        put(self.res_var, "res", list(RES_SIZE_MODE) + list(RES_BITRATE_MODE))
        put(self.codec_var, "codec", CODECS)
        put(self.crop_var, "crop", list(ASPECTS))
        put(self.audio_var, "audio", ["48k", "64k", "96k", "128k", "192k"])
        put(self.atrk_var, "atrk", ["Keep separate tracks", "Mix into one track"])
        put(self.mode_var, "mode", ["separate", "join"])
        put(self.trans_var, "trans", list(TRANSITIONS))
        put(self.tlen_var, "tlen")
        put(self.fast_aud_var, "fast_audio", FAST_AUDIO)
        put(self.slow_aud_var, "slow_audio", SLOW_AUDIO)
        put(self.pitch_var, "slow_pitch", SLOW_PITCH)
        put(self.fast_pitch_var, "fast_pitch", FAST_PITCH)
        put(self.zoom_fx_var, "zoom_fx", ZOOM_FX)
        put(self.zoom_snd_var, "zoom_snd", ZOOM_AUDIO)
        if isinstance(st.get("zoom_len"), (str, int, float)) and not isinstance(st.get("zoom_len"), bool):
            self.zoom_len_var.set(str(st["zoom_len"]))
        if isinstance(st.get("zoom_vol"), int) and not isinstance(st.get("zoom_vol"), bool):
            self.zoom_vol_var.set(max(0, min(200, st["zoom_vol"])))
        for _v, _k in ((self.fast_vol_var, "fast_vol"), (self.slow_vol_var, "slow_vol")):
            if isinstance(st.get(_k), int) and not isinstance(st.get(_k), bool):
                _v.set(max(0, min(200, st[_k])))
        put(self.interp_var, "slow_interp", SLOW_INTERP)
        for var, key in ((self.mutemic_var, "mute_mic"), (self.vhs_var, "vhs"), (self.hdr_var, "hdr")):
            if isinstance(st.get(key), bool):
                var.set(st[key])
        self._bitrate_changed()
        self._mode_changed()

    def _update_title(self):
        name = f" - {os.path.basename(self.project_path)}" if self.project_path else ""
        self.title(f"{APP_NAME} v{APP_VERSION}{name}")

    def _restore_session(self):
        if os.path.isfile(SESSION_FILE) and not self.rows:
            self.load_project(SESSION_FILE, quiet=True)

    def _save_session(self):
        try:
            if self.restore_var.get() and self.rows:
                with open(SESSION_FILE, "w", encoding="utf-8") as f:
                    json.dump(self.project_data(), f, indent=1)
            elif os.path.isfile(SESSION_FILE) and not self.restore_var.get():
                os.remove(SESSION_FILE)
        except Exception:
            pass

    def remove_row(self, row):
        if self.encoding:
            return
        self.stop_playback()
        self.push_undo("remove clip")
        self._detach_row(row)
        idx = self.rows.index(row)
        self.rows.remove(row)
        self.selection.discard(row)
        if self.active_row is row:
            self.active_row = None
            if self.rows:                        # the next clip, or the previous one if it was the last
                self.set_active(self.rows[min(idx, len(self.rows) - 1)])
        if self._last_preview and self._last_preview[0] is row:
            self._last_preview = None
            self.preview.config(image="", text="Add a video to see the preview")
        self._renumber()

    def move_row(self, row, d):
        if self.encoding:
            return
        i = self.rows.index(row)
        j = i + d
        if 0 <= j < len(self.rows):
            self.rows[i], self.rows[j] = self.rows[j], self.rows[i]
            for r in self.rows:
                r.pack_forget()
            for r in self.rows:
                r.pack(fill="x", pady=(0, 8), padx=2)
            self._renumber()

    def drag_row(self, row, y_root):
        """Live reordering while a clip's title line is dragged: the row goes where the pointer is."""
        if self.encoding or row not in self.rows:
            return
        top = self.list_canvas.winfo_rooty()
        if y_root < top + 24:
            self.list_canvas.yview_scroll(-1, "units")
        elif y_root > top + self.list_canvas.winfo_height() - 24:
            self.list_canvas.yview_scroll(1, "units")
        idx = sum(1 for r in self.rows if r is not row and r.winfo_rooty() + r.winfo_height() / 2 < y_root)
        if idx == self.rows.index(row):
            return
        if not self._reorder_pushed:
            self._reorder_pushed = True
            self.push_undo("reorder clips")
        self.rows.remove(row)
        self.rows.insert(idx, row)
        if idx + 1 < len(self.rows):
            row.pack_configure(before=self.rows[idx + 1])
        else:
            row.pack_configure(after=self.rows[idx - 1])
        self._renumber()

    def clear_all(self):
        if self.encoding:
            return
        self.stop_playback()
        if self.rows:
            self.push_undo("clear all")
        for r in list(self.rows):
            self._detach_row(r)
        self.rows.clear()
        self.selection = set()
        self.active_row = None
        self._last_preview = None
        self.preview.config(image="", text="Add a video to see the preview")
        self._renumber()

    def _renumber(self):
        for i, r in enumerate(self.rows):
            r.set_index(i)
        if self.rows:
            if not self.add_strip.winfo_manager():
                self.add_strip.pack(side="bottom", fill="x", pady=(4, 2), before=self._strip_before)
        else:
            self.add_strip.pack_forget()
        if not self.rows:
            self._update_summary()
            self.empty_lbl.pack(fill="x", padx=4, pady=6)
            self.count_lbl.config(text="Add videos with the button or drag & drop them onto this window")
        else:
            self._update_total()
        self._update_crop_info()

    # ---------------------------------------------- transition preview (hover in the dropdown list)
    def _trans_sources(self):
        """Inputs for the preview: the first two clips (as they would be joined), else the active clip's
        start and end, else plain colours."""
        rows = list(self.rows)
        if len(rows) >= 2:
            a, b = rows[0], rows[1]
            return [(a.path, max(a.start, a.end - 1.2)), (b.path, b.start)]
        if rows:
            r = rows[0]
            return [(r.path, r.start), (r.path, max(r.start, r.end - 1.2))]
        return None

    def _trans_frames(self, name):
        """Renders ~1.6 s of the transition at preview size -> list of PPM byte strings (cached)."""
        src = self._trans_sources()
        key = (name, repr(src))
        cache = self.__dict__.setdefault("_tpv_cache", {})
        if key in cache:
            return cache[key]
        xf = TRANSITIONS.get(name)
        W, H, F = PV_W, PV_H, PV_FPS
        one = f"scale={W}:{H}:force_original_aspect_ratio=decrease,pad={W}:{H}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={F},format=yuv420p"
        cmd = [FFMPEG, "-hide_banner", "-loglevel", "error"]
        if src:
            for p, t in src:
                cmd += ["-ss", f"{t:.3f}", "-t", "1.3", "-i", p]
            ga = f"[0:v:0]{one},trim=duration=1.2,setpts=PTS-STARTPTS[a];[1:v:0]{one},trim=duration=1.2,setpts=PTS-STARTPTS[b]"
        else:
            ga = (f"color=c=0x2f6db5:s={W}x{H}:r={F}:d=1.2,format=yuv420p[a];"
                  f"color=c=0xe08a2c:s={W}x{H}:r={F}:d=1.2,format=yuv420p[b]")
        if xf == GLITCH:
            g = ga + f";[a][b]xfade=transition=fadefast:duration=0.8:offset=0.4[g0];" + \
                ";".join(glitch_parts("g0", "g1", 0.4, 0.8, W, H, "pg")) + ";[g1]format=rgb24[o]"
        elif xf:
            g = ga + f";[a][b]xfade=transition={xf}:duration=0.8:offset=0.4,format=rgb24[o]"
        else:
            g = ga + ";[a]trim=duration=0.8[a2];[b]trim=duration=0.8,setpts=PTS-STARTPTS[b2];[a2][b2]concat=n=2:v=1:a=0,format=rgb24[o]"
        cmd += ["-filter_complex", g, "-map", "[o]", "-frames:v", str(int(1.6 * F)), "-f", "rawvideo", "pipe:1"]
        frames = []
        try:
            out = run_capture(cmd, binary=True).stdout or b""
            n = W * H * 3
            hdr = f"P6 {W} {H} 255\n".encode()
            frames = [hdr + out[i:i + n] for i in range(0, len(out) - n + 1, n)]
        except Exception as e:
            _log(f"transition preview failed: {e}")
        if frames:
            cache[key] = frames
        return frames

    def _trans_hover_setup(self, cb):
        """Shows a small looping preview next to the combobox list while the mouse is over an entry."""
        try:
            pd = self.tk.eval(f"ttk::combobox::PopdownWindow {cb}")
        except tk.TclError:
            return
        lb = pd + ".f.l"
        names = list(TRANSITIONS)
        state = {"win": None, "lbl": None, "name": None, "job": None, "frames": [], "i": 0, "imgs": []}

        def hide(*a):
            if state["job"]:
                try:
                    self.after_cancel(state["job"])
                except Exception:
                    pass
                state["job"] = None
            if state["win"] is not None:
                state["win"].withdraw()
            state["name"] = None

        def ensure_win():
            if state["win"] is None:
                w = tk.Toplevel(self)
                w.withdraw()
                w.overrideredirect(True)
                try:
                    w.attributes("-topmost", True)
                except tk.TclError:
                    pass
                f = tk.Frame(w, bg=self.pal.get("accent", "#3a7bd5") if isinstance(self.pal, dict) else "#3a7bd5",
                             padx=2, pady=2)
                f.pack()
                blank = tk.PhotoImage(width=PV_W, height=PV_H)
                lbl = tk.Label(f, bd=0, bg="#000000", image=blank)
                lbl.image = blank
                lbl.pack()
                cap = tk.Label(f, bd=0, bg="#1c1c1c", fg="#dddddd", font=("Segoe UI", 8), anchor="w", padx=4)
                cap.pack(fill="x")
                state.update(win=w, lbl=lbl, cap=cap)
            return state["win"]

        def tick():
            fr = state["imgs"]
            if not fr or state["win"] is None:
                return
            state["lbl"].config(image=fr[state["i"] % len(fr)])
            state["i"] += 1
            delay = 1000 // PV_FPS
            if state["i"] % len(fr) == 0:
                delay = 500                                   # short pause before it loops
            state["job"] = self.after(delay, tick)

        def show(name):
            if name == state["name"]:
                return
            hide()
            state["name"] = name
            w = ensure_win()
            state["imgs"] = []
            state["lbl"].config(image=state["lbl"].image)
            state["cap"].config(text=f"{name}  (rendering...)")
            # place it to the right of the open list (or to the left if there's no room)
            try:
                x = int(self.tk.eval(f"winfo rootx {pd}")) + int(self.tk.eval(f"winfo width {pd}")) + 6
                y = int(self.tk.eval(f"winfo rooty {lb}"))
                if x + PV_W + 10 > self.winfo_screenwidth():
                    x = int(self.tk.eval(f"winfo rootx {pd}")) - PV_W - 10
                w.geometry(f"+{x}+{y}")
            except tk.TclError:
                pass
            w.deiconify()
            w.lift()

            def work():
                frames = self._trans_frames(name)
                def done():
                    if state["name"] != name:
                        return
                    state["imgs"] = [tk.PhotoImage(data=d, format="PPM") for d in frames]
                    state["i"] = 0
                    state["cap"].config(text=name if frames else f"{name}  (no preview)")
                    tick()
                try:
                    self.after(0, done)
                except Exception:
                    pass
            threading.Thread(target=work, daemon=True).start()

        def motion(y):
            try:
                i = int(self.tk.eval(f"{lb} nearest {y}"))
            except (tk.TclError, ValueError):
                return
            if 0 <= i < len(names):
                show(names[i])

        cmd_m = self.register(motion)
        cmd_h = self.register(hide)
        self.tk.eval(f"bind {lb} <Motion> {{+{cmd_m} %y}}")
        self.tk.eval(f"bind {lb} <Leave> {{+{cmd_h}}}")
        self.tk.eval(f"bind {pd} <Unmap> {{+{cmd_h}}}")
        cb.bind("<Destroy>", lambda e: (hide(), state["win"] is not None and state["win"].destroy()), add="+")

    def _update_total(self):
        """Clip count + total length after trimming AND speed-up / slow-mo sections."""
        if not self.rows:
            return
        try:
            total = sum(pieces_duration(get_pieces(r.clip())) for r in self.rows)
        except Exception:
            total = sum(r.end - r.start for r in self.rows)
        txt = f"{len(self.rows)} clip(s), {fmt_time(total)} in total after edits"
        if self.count_lbl.cget("text") != txt:
            self.count_lbl.config(text=txt)
            self._update_summary()

    # ------------------------------------------------------------ preview
    def _pv_dims(self):
        w, h = self.preview_box.winfo_width(), self.preview_box.winfo_height()
        if w < 50 or h < 50:
            w, h = PREVIEW_W, PREVIEW_H
        return min(w, MAX_PV[0]), min(h, MAX_PV[1])

    def _on_preview_resize(self, e):
        if self._resize_job:
            self.after_cancel(self._resize_job)
        self._resize_job = self.after(200, self._preview_resized)

    def _preview_resized(self):
        self._resize_job = None
        if self.playing:
            self.start_playback()            # restart at the new size
        elif self._last_preview and self._last_preview[0] in self.rows:
            self.request_preview(*self._last_preview)

    def set_active(self, row):
        if self.active_row is row:
            return
        self.after(80, self.bracket_refresh)
        old, self.active_row = self.active_row, row
        for r in (old, row):
            if r is not None and r in self.rows:
                r.set_active(r is row)
        self.selection = {row}
        self._refresh_selection()
        self.after(60, lambda: self._reveal_row(row))

    # ------------------------------------------------------------ undo / redo
    UNDO_MAX = 60

    def _snap(self):
        return {"rows": list(self.rows), "active": self.active_row if self.active_row in self.rows else None,
                "state": {r: (r.start, r.end, [(x["a"], x["b"], x["speed"])
                                               for x in sorted(r.sections, key=lambda x: x["a"])], r.preset,
                             [(z["a"], z["b"], z["x"], z["y"], z["w"]) for z in sorted(r.zooms, key=lambda z: z["a"])])
                          for r in self.rows}}

    @staticmethod
    def _sig(snap):
        return (tuple(id(r) for r in snap["rows"]), id(snap["active"]),
                tuple((id(r), round(v[0], 3), round(v[1], 3), tuple((round(a, 3), round(b, 3), sp) for a, b, sp in v[2]), v[3],
                      tuple(tuple(round(q, 3) for q in z) for z in v[4]))
                      for r, v in snap["state"].items()))

    def push_undo(self, label):
        if self.encoding or self._undo_off:
            return False
        self._undo_serial += 1
        self.undo_stack.append((label, self._snap()))
        if len(self.undo_stack) > self.UNDO_MAX:
            del self.undo_stack[0]
        self.redo_stack.clear()
        self._gc_rows()
        self._update_undo_btns()
        return True

    def _gc_rows(self):
        """Hidden clips that no undo / redo step refers to any more are really removed."""
        used = set(self.rows)
        for _, sn in self.undo_stack + self.redo_stack:
            used.update(sn["rows"])
        for r in list(self._detached):
            if r not in used:
                self._detached.discard(r)
                try:
                    r.dispose()
                    r.destroy()
                except Exception:
                    pass

    def _update_undo_btns(self):
        try:
            self.undo_btn.config(state="normal" if self.undo_stack else "disabled")
            self.redo_btn.config(state="normal" if self.redo_stack else "disabled")
        except Exception:
            pass

    def _detach_row(self, r):
        r.detach()
        r.pack_forget()
        self._detached.add(r)

    def undo(self):
        self._step(self.undo_stack, self.redo_stack, "Undo")

    def redo(self):
        self._step(self.redo_stack, self.undo_stack, "Redo")

    def _step(self, src, dst, verb):
        if self.encoding:
            return
        cur = self._snap()
        entry = None
        while src:
            label, snap = src.pop()
            if self._sig(snap) != self._sig(cur):
                entry = (label, snap)
                break
        if entry is None:
            self._update_undo_btns()
            self.set_status(f"Nothing to {verb.lower()}")
            return
        dst.append((entry[0], cur))
        self._restore(entry[1])
        self._gc_rows()
        self._update_undo_btns()
        self.set_status(f"{verb}: {entry[0]}")

    def _restore(self, snap):
        self.stop_playback()
        keep = set(snap["rows"])
        for r in self.rows:
            if r not in keep:
                self._detach_row(r)
        for r in self.rows:
            r.pack_forget()
        self.rows = list(snap["rows"])
        self.empty_lbl.pack_forget()
        for r in self.rows:
            if r in self._detached:
                self._detached.discard(r)
                r.reset_id += 1
                r.refresh_thumbs()
            st = snap["state"].get(r)
            if st:
                r.set_state(*st)
            r.pack(fill="x", pady=(0, 8), padx=2)
        target = snap["active"] if snap["active"] in self.rows else (self.rows[0] if self.rows else None)
        self.active_row = None
        for r in self.rows:
            r.set_active(False)
        if target is not None:
            self.set_active(target)
            self.request_preview(target, target.start)
        else:
            self._last_preview = None
            self.preview.config(image="", text="Add a video to see the preview")
        self.selection = {target} if target else set()
        self._refresh_selection()
        self._renumber()
        self._update_crop_info()
        self.after(100, self.bracket_refresh)

    def _refresh_selection(self):
        for r in self.rows:
            r.set_selected(r in self.selection)

    def click_row(self, row, state=0):
        """Click on a clip: plain = select only it, Shift = range from the active clip, Ctrl = add / remove."""
        if row not in self.rows:
            return
        shift, ctrl = bool(state & 0x1), bool(state & 0x4)
        act = self.active_row if self.active_row in self.rows else None
        if shift and act is not None:
            i, j = sorted((self.rows.index(act), self.rows.index(row)))
            self.selection = set(self.rows[i:j + 1])
        elif ctrl and act is not None:
            if row is not act:
                self.selection = self.selection ^ {row}
            self.selection.add(act)
        else:
            if self.active_row is row:
                self.selection = {row}
            else:
                self.set_active(row)
        self._refresh_selection()
        n = len(self.selection)
        if n > 1:
            self.set_status(f"{n} clips selected - press Delete to remove them")

    def select_all(self):
        if self.rows:
            if self.active_row not in self.rows:
                self.set_active(self.rows[0])
            self.selection = set(self.rows)
            self._refresh_selection()
            self.set_status(f"{len(self.rows)} clips selected - press Delete to remove them")

    def delete_selected(self):
        """Removes every selected clip (the active one is always part of the selection)."""
        if self.encoding:
            return
        victims = [r for r in self.rows if r in self.selection or r is self.active_row]
        if not victims:
            return
        self.stop_playback()
        first = self.rows.index(victims[0])
        self.push_undo(f"remove {len(victims)} clip(s)")
        for r in victims:
            self._detach_row(r)
            self.rows.remove(r)
        if self._last_preview and self._last_preview[0] in victims:
            self._last_preview = None
            self.preview.config(image="", text="Add a video to see the preview")
        self.active_row = None
        self.selection = set()
        if self.rows:
            nxt = self.rows[min(first, len(self.rows) - 1)]
            self.set_active(nxt)
            self.request_preview(nxt, nxt.start)
        self._renumber()
        self.set_status(f"Removed {len(victims)} clip(s)")

    def _on_delete(self, e):
        if self.encoding or self._typing() or not self.rows:
            return
        row = self.active_row
        if row in self.rows and row.sel is not None and row.sel in row.sections:
            row.remove_section(row.sel)                     # a selected speed section goes first
        elif row in self.rows and row.zsel is not None and row.zsel in row.zooms:
            row.remove_zoom(row.zsel)
        else:
            self.delete_selected()
        return "break"

    def _on_backspace(self, e):
        if self.encoding or self._typing():
            return
        row = self.active_row
        if row in self.rows and row.sel is not None and row.sel in row.sections:
            row.remove_section(row.sel)
            return "break"
        if row in self.rows and row.zsel is not None and row.zsel in row.zooms:
            row.remove_zoom(row.zsel)
            return "break"

    def _reveal_row(self, row):
        """Keeps the (just enlarged) active clip inside the visible part of the list."""
        try:
            if row not in self.rows or row is not self.active_row:
                return
            self.update_idletasks()
            tot = max(1, self.list_inner.winfo_height(), self.list_canvas.winfo_height())
            y, h = row.winfo_y(), row.winfo_height()
            top, vh = self.list_canvas.canvasy(0), self.list_canvas.winfo_height()
            if y < top or h > vh:
                self.list_canvas.yview_moveto(y / tot)
            elif y + h > top + vh:
                self.list_canvas.yview_moveto((y + h - vh) / tot)
        except Exception:
            pass

    def request_preview(self, row, t):
        self._last_preview = (row, t)
        self.set_active(row)
        i = row.info
        cw, ch = crop_size(i["w"], i["h"], self._ratio())
        crop = (cw, ch) if (cw, ch) != (i["w"], i["h"]) else None
        w, h = self._pv_dims()
        trc = row.info["trc"] if self.hdr_var.get() else None
        self._pending_preview = (row.path, t, crop, w, h, trc)
        if not self._preview_busy:
            self._preview_busy = True
            threading.Thread(target=self._preview_loop, daemon=True).start()

    def _preview_loop(self):
        while self._pending_preview is not None:
            (path, t, crop, w, h, trc), self._pending_preview = self._pending_preview, None
            data = grab_frame(path, t, w, h, crop, trc)
            if data:
                self.after(0, self._show_preview, data)
        self._preview_busy = False

    def _show_preview(self, data):
        if self.playing:
            return
        self.preview_img = tk.PhotoImage(data=data)
        self.preview.config(image=self.preview_img, text="")
        self.after(40, self.bracket_refresh)

    # ----------------------------------------------------------- zoom bracket
    def _build_bracket(self):
        self._br_lines = [tk.Frame(self.preview_box, bg="#ffffff", highlightthickness=1,
                                   highlightbackground="#000000", cursor="fleur") for _ in range(4)]
        self._br_corners = {}
        cur = {"nw": "size_nw_se", "se": "size_nw_se", "ne": "size_ne_sw", "sw": "size_ne_sw"}
        for k in ("nw", "ne", "sw", "se"):
            f = tk.Frame(self.preview_box, bg="#ffffff", highlightthickness=1, highlightbackground="#000000",
                         width=11, height=11)
            try:
                f.configure(cursor=cur[k])
            except Exception:
                pass
            f.bind("<Button-1>", lambda e, k=k: self._br_down(e, k))
            f.bind("<B1-Motion>", self._br_move)
            f.bind("<ButtonRelease-1>", self._br_up)
            self._br_corners[k] = f
        for f in self._br_lines:
            f.bind("<Button-1>", lambda e: self._br_down(e, "move"))
            f.bind("<B1-Motion>", self._br_move)
            f.bind("<ButtonRelease-1>", self._br_up)
        self.preview.bind("<Button-1>", self._pv_click)
        self.preview.bind("<B1-Motion>", self._br_move)
        self.preview.bind("<ButtonRelease-1>", self._br_up)
        self._br = None
        self._br_vis = False

    def _br_target(self):
        """(zoom section, label rect) when the bracket is to be shown, else None."""
        row = self.active_row
        if self.playing or row not in self.rows or not row.zooms:
            return None
        ph = row.playhead
        inside = [z for z, a, b in row.vis_z() if a - 1e-3 <= ph <= b + 1e-3]
        if not inside:
            return None                                      # playhead outside every zoom section: no bracket
        z = row.zsel if row.zsel in inside else inside[0]
        try:
            if not self.preview.cget("image"):
                return None
            pw, ph = self.preview.winfo_width(), self.preview.winfo_height()
            if pw < 20 or ph < 20:
                return None
            return z, (self.preview.winfo_x(), self.preview.winfo_y(), pw, ph)
        except Exception:
            return None

    def bracket_refresh(self):
        if not hasattr(self, "_br_lines"):
            return
        tg = self._br_target()
        if tg is None:
            if self._br_vis:
                for f in self._br_lines + list(self._br_corners.values()):
                    f.place_forget()
                self._br_vis = False
            return
        z, (px, py, pw, ph) = tg
        bx, by, bw, bh = px + z["x"] * pw, py + z["y"] * ph, z["w"] * pw, z["w"] * ph
        t = 4
        L = self._br_lines
        L[0].place(x=bx, y=by, width=bw, height=t)
        L[1].place(x=bx, y=by + bh - t, width=bw, height=t)
        L[2].place(x=bx, y=by, width=t, height=bh)
        L[3].place(x=bx + bw - t, y=by, width=t, height=bh)
        for k, (cx, cy) in {"nw": (bx, by), "ne": (bx + bw, by), "sw": (bx, by + bh), "se": (bx + bw, by + bh)}.items():
            self._br_corners[k].place(x=cx - 6, y=cy - 6, width=13, height=13)
        for f in L + list(self._br_corners.values()):
            try:
                tk.Misc.lift(f)
            except Exception:
                pass
        self._br_vis = True

    def _pv_click(self, e):
        tg = self._br_target()
        if tg is None:
            return
        z, (px, py, pw, ph) = tg
        if z["x"] * pw <= e.x <= (z["x"] + z["w"]) * pw and z["y"] * ph <= e.y <= (z["y"] + z["w"]) * ph:
            self._br_down(e, "move")

    def _br_down(self, e, mode):
        tg = self._br_target()
        if tg is None:
            return
        z = tg[0]
        self.push_undo("zoom area")
        self._br = {"mode": mode, "z": z, "x0": e.x_root, "y0": e.y_root, "orig": (z["x"], z["y"], z["w"])}

    def _br_move(self, e):
        st = self._br
        tg = self._br_target()
        if not st or tg is None:
            return
        z, (px, py, pw, ph) = tg
        ox, oy, ow = st["orig"]
        if st["mode"] == "move":
            z["x"] = min(max(ox + (e.x_root - st["x0"]) / pw, 0.0), 1 - ow)
            z["y"] = min(max(oy + (e.y_root - st["y0"]) / ph, 0.0), 1 - ow)
        else:
            k = st["mode"]
            sx, sy = (1 if k[1] == "e" else -1), (1 if k[0] == "s" else -1)
            ax, ay = (ox if sx > 0 else ox + ow), (oy if sy > 0 else oy + ow)         # the opposite corner stays
            fx = (e.x_root - self.preview.winfo_rootx()) / pw
            fy = (e.y_root - self.preview.winfo_rooty()) / ph
            w = max(sx * (fx - ax), sy * (fy - ay))
            wmax = min((1 - ax) if sx > 0 else ax, (1 - ay) if sy > 0 else ay)
            w = min(max(w, ZOOM_MIN_W), wmax)
            z["w"] = w
            z["x"] = ax if sx > 0 else ax - w
            z["y"] = ay if sy > 0 else ay - w
        self.bracket_refresh()

    def _br_up(self, e=None):
        self._br = None

    # ----------------------------------------------------------- playback
    def _typing(self):
        try:
            w = self.focus_get()
        except Exception:
            w = None
        if w is not None:
            cls = w.winfo_class()
            if cls in ("TEntry", "Entry", "TSpinbox", "Spinbox", "Text"):
                return True                             # typing a value, not a shortcut
            if cls == "TCombobox" and str(w.cget("state")) != "readonly":
                return True
        return False

    def _on_space(self, e):
        if self.encoding or self._typing():
            return
        self.toggle_play()
        return "break"

    def _pv_vol(self, v):
        n = int(round(float(v)))
        if n != self.pv_vol_var.get():
            self.pv_vol_var.set(n)
        PV["vol"] = n / 100.0

    def _spk_icon(self):
        try:
            self.spk_btn.configure(image=self._icon("spk" if self.sound_var.get() else "spk_off"))
        except Exception:
            pass

    def _on_mute(self, e=None, force=False):
        if self._typing() and not force:
            return
        on = not self.sound_var.get()
        self.sound_var.set(on)
        w = self._wave
        if self.playing:
            if w is not None:
                w.set_muted(not on)                         # instant: the audio stream just goes silent
            elif on and winsound is not None and self.play_row in self.rows and self.play_row.info["audio"]:
                self.start_playback()                       # no audio stream yet: restart with sound
            elif not on:
                self._stop_wave()
                if winsound is not None:
                    try:
                        winsound.PlaySound(None, winsound.SND_PURGE)
                    except Exception:
                        pass
        self.set_status("Preview sound on" if on else "Preview sound muted")
        return "break"

    def _on_trim(self, which):
        if self.encoding or self._typing() or not self.rows:
            return
        row = self.active_row if self.active_row in self.rows else self.rows[0]
        row.set_trim(which)
        return "break"

    def _on_export_key(self, e=None):
        if self.encoding or self._typing():
            return
        self.start_export()
        return "break"

    def _on_s(self, e, speed=2.0):
        if self.encoding or self._typing() or not self.rows:
            return
        row = self.active_row if self.active_row in self.rows else self.rows[0]
        row.add_section(speed)
        return "break"

    def _on_z(self, e):
        if self.encoding or self._typing() or not self.rows:
            return
        row = self.active_row if self.active_row in self.rows else self.rows[0]
        row.add_zoom()
        return "break"

    def toggle_play(self):
        if self.playing:
            self.stop_playback()
        else:
            self.start_playback()

    def start_playback(self):
        if self.encoding or not self.rows:
            return
        row = self.active_row if self.active_row in self.rows else self.rows[0]
        self.stop_playback()
        cleanup_wavs()
        self._wcache = {}
        self.set_active(row)
        i = row.info
        cw, ch = crop_size(i["w"], i["h"], self._ratio())
        crop = (cw, ch) if (cw, ch) != (i["w"], i["h"]) else None
        bw, bh = self._pv_dims()
        scale = min(bw / cw, bh / ch, 1.0)
        w, h = even(cw * scale), even(ch * scale)
        fps = min(float(PLAY_FPS_CAP), i["fps"])
        self.play_gen += 1
        gen = self.play_gen
        self.playing = True
        self.play_row = row
        self._play_dims = (w, h)
        self._frame, self._draw_pending = None, False
        self.play_btn.config(image=self._icon("pause"))
        self.bracket_refresh()
        use_sound = bool(self.sound_var.get() and winsound is not None and i["audio"])
        # first pass starts at the playhead (selection point); later passes loop from the start handle
        t0 = row.playhead if row.start <= row.playhead < row.end - 0.25 else row.start
        secs = [{"a": a, "b": b, "speed": sec["speed"]} for sec, a, b in row.vis()]
        ZOOM["play"] = [(a, b, z["x"], z["y"], z["w"]) for z, a, b in row.vis_z()]
        threading.Thread(target=self._play_thread, daemon=True,
                         args=(gen, row.path, row.start, row.end, t0, crop, w, h, fps, use_sound,
                               i["trc"] if self.hdr_var.get() else None, secs, self.fast_aud_var.get() + "|" + self.slow_aud_var.get() + "|" + self.pitch_var.get() + "|" + self.fast_pitch_var.get(),
                               1 if self.mutemic_var.get() else i.get("naudio", 1))).start()

    def stop_playback(self):
        was = self.playing
        self.playing = False
        with self._snd_lock:
            self.play_gen += 1
        p = self.play_proc
        if p is not None and p.poll() is None:
            try:
                p.kill()
            except OSError:
                pass
        self._stop_wave()
        if winsound is not None:
            try:
                winsound.PlaySound(None, winsound.SND_PURGE)
            except Exception:
                pass
        self.play_btn.config(image=self._icon("play"))
        if was:
            row = self.active_row
            if row in self.rows and row.zsel is not None:
                self.after(50, lambda: self.request_preview(row, row.playhead))   # unzoomed frame under the bracket

    # Audio is cut into a short first chunk (ready at once) and the remainder (built in the background),
    # so the video never waits for audio extraction.  Chunks are cached for the following loop passes.
    FIRST_CHUNK = 4.0

    def _audio_prepare(self, gen, path, a, end, secs, mute, naud):
        """Starts building the audio chunks for [a, end]; returns [(ev, holder, out_duration)]."""
        cuts = [a, end] if end - a <= self.FIRST_CHUNK + 1.0 else [a, a + self.FIRST_CHUNK, end]
        cache = self._wcache
        sig = tuple((round(x["a"], 3), round(x["b"], 3), x["speed"]) for x in secs) + (mute, naud) + \
              tuple((round(z[0], 3), round(z[1], 3)) for z in ZOOM["play"]) + (ZOOM["sound"], ZOOM["vol"], ZOOM["len"], ZOOM["motion"])
        feed = []
        for k in range(len(cuts) - 1):
            ca, cb = cuts[k], cuts[k + 1]
            pieces = build_pieces(ca, cb, secs)
            key = (path, round(ca, 3), round(cb, 3), sig)
            ent = cache.get(key)
            if ent is None:
                ent = cache[key] = {"ev": threading.Event(), "wav": None}
                ent["todo"] = (ca, cb, pieces)
            feed.append((ent, pieces_duration(pieces)))

        def build():
            for ent, _ in feed:
                todo = ent.pop("todo", None)
                if todo is None:
                    continue
                ca, cb, pieces = todo
                try:
                    if gen == self.play_gen:
                        ent["wav"] = make_preview_wav(path, ca, cb - ca, f"{gen}_{id(ent) & 0xffff}",
                                                      pieces, mute, naud)
                finally:
                    ent["ev"].set()
        threading.Thread(target=build, daemon=True).start()
        return feed

    def _audio_play(self, gen, feed, clock):
        """Plays the chunks back to back, following the moment the first video frame was shown."""
        if winsound is None:
            return
        pos = 0.0                                   # output-time start of the current chunk
        for ent, dur in feed:
            if gen != self.play_gen:
                return
            ent["ev"].wait(60)
            wav = ent["wav"]
            late = time.perf_counter() - clock - pos
            if wav and late < dur - 0.1:
                if late < -0.02:
                    time.sleep(-late)               # wait for the chunk boundary
                elif late > 0.05:                   # audio was late: join mid-chunk
                    wav = slice_wav(wav, late, f"{gen}_s{id(ent) & 0xffff}")
                with self._snd_lock:
                    if gen != self.play_gen:
                        return
                    try:
                        winsound.PlaySound(wav, winsound.SND_FILENAME | winsound.SND_ASYNC)
                    except Exception:
                        pass
            pos += dur
            # wait (in small steps) until this chunk is about over before the next one is started
            while gen == self.play_gen and time.perf_counter() - clock < pos - 0.02:
                time.sleep(min(0.05, max(0.0, pos - (time.perf_counter() - clock) - 0.02)))

    def _stop_wave(self):
        w, self._wave = self._wave, None
        if w is not None:
            w.stop()
        p = getattr(self, "_aproc", None)
        if p is not None and p.poll() is None:
            try:
                p.kill()
            except OSError:
                pass

    def _wave_begin(self, gen, path, a, end, secs, mute, naud):
        """Streams the audio of [a, end] through waveOut (paused until the first frame). False = not possible."""
        try:
            player = WaveOut()
        except Exception:
            if _LOG:
                traceback.print_exc(file=_LOG)
            self._wave_ok = False
            return False
        cmd = preview_audio_cmd(path, a, end - a, build_pieces(a, end, secs), mute, naud, None)
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                stdin=subprocess.DEVNULL, creationflags=NO_WINDOW)
        with self._snd_lock:
            if gen != self.play_gen:
                proc.kill()
                player.stop()
                return False
            self._stop_wave()
            self._aproc, self._wave = proc, player
        if not self.sound_var.get():
            player.set_muted(True)
        player.feed(proc.stdout)
        return True

    def _play_thread(self, gen, path, start, end, t0, crop, w, h, fps, use_sound, trc=None, secs=(), mute=True, naud=1):
        try:
            self._play_loop(gen, path, start, end, t0, crop, w, h, fps, use_sound, trc, secs, mute, naud)
        except Exception:
            if _LOG:
                traceback.print_exc(file=_LOG)
            if gen == self.play_gen:
                self.after(0, self._play_failed)

    def _play_loop(self, gen, path, start, end, t0, crop, w, h, fps, use_sound, trc=None, secs=(), mute=True, naud=1):
        fsize = w * h * 3
        first_pass = True
        while gen == self.play_gen:
            if not first_pass and self.play_row in self.rows:        # markers may have been moved meanwhile
                start, end = self.play_row.start, self.play_row.end
            pstart = t0 if first_pass else start
            feed = None
            feed, wave_on = None, False
            if use_sound:
                if self._wave_ok:
                    wave_on = self._wave_begin(gen, path, pstart, end, secs, mute, naud)
                if not wave_on:                             # fallback: winsound with WAV chunks
                    feed = self._audio_prepare(gen, path, pstart, end, secs, mute, naud)
            n = 0
            pieces = build_pieces(pstart, end, secs)
            for lvl in range(self._play_lvl, -1, -1):
                cmd = play_command(path, pstart, end - pstart, crop, w, h, fps, lvl, trc, pieces)
                t_launch = time.perf_counter()
                proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        stdin=subprocess.DEVNULL, creationflags=NO_WINDOW)
                err = []
                threading.Thread(target=lambda: err.append(proc.stderr.read()[-1500:]), daemon=True).start()
                self.play_proc = proc
                n = 0
                t_start = None
                try:
                    while gen == self.play_gen:
                        buf = read_exact(proc.stdout, fsize)
                        if buf is None:
                            break
                        now = time.perf_counter()
                        if t_start is None:
                            t_start = now
                            if _LOG:
                                _LOG.write(f"preview: level {lvl} first frame after {now - t_launch:.2f} s\n")
                            if wave_on and self._wave is not None:
                                self._wave.start()
                            if feed:
                                threading.Thread(target=self._audio_play, args=(gen, feed, now),
                                                 daemon=True).start()
                        due = t_start + n / fps             # real-time pacing
                        if due > now:
                            time.sleep(due - now)
                        elif n > 0 and now - due > 0.15:    # far behind: drop frames to catch up
                            n += 1
                            continue
                        ts = out_to_src(pieces, n / fps)
                        if self.play_row in self.rows and ts > self.play_row.end + 0.03:
                            break                           # the end marker was moved back: loop now
                        self._deliver(gen, buf, ts)
                        n += 1
                finally:
                    if proc.poll() is None:
                        proc.kill()
                    proc.stdout.close()
                    proc.wait()
                if _LOG and n == 0:
                    _LOG.write(f"preview: level {lvl} gave no frames after {time.perf_counter() - t_launch:.2f} s: "
                               f"{(err[0] if err else b'').decode(errors='replace')}\n")
                if gen != self.play_gen:
                    return
                if n > 0:
                    break
                if lvl == self._play_lvl and lvl > 0:
                    self._play_lvl = lvl - 1                # that decoding mode is unavailable - step down
            if n == 0:
                self.after(0, self._play_failed)
                return
            first_pass = False

    def _deliver(self, gen, buf, t):
        with self._frame_lock:
            self._frame = (gen, buf, t)
            if self._draw_pending:
                return                              # the screen is behind: keep only the newest frame
            self._draw_pending = True
        self.after(0, self._draw_frame)

    def _draw_frame(self):
        with self._frame_lock:
            fr, self._frame = self._frame, None
            self._draw_pending = False
        if fr is None or fr[0] != self.play_gen or not self.playing:
            return
        _, buf, t = fr
        w, h = self._play_dims
        img = tk.PhotoImage(data=b"P6 %d %d 255\n" % (w, h) + buf, format="ppm")
        self.preview.config(image=img, text="")
        self.preview_img = img
        if self.play_row in self.rows:
            self.play_row.set_playhead_fast(t)

    def _play_failed(self):
        self.stop_playback()
        self._toast("Could not play the preview (details in clipshrinker.log)")

    def on_close(self):
        if self.rows:
            n = len(self.rows)
            d = ChoiceDialog(self, "Close Clip Shrinker",
                             f"{n} clip{'s are' if n != 1 else ' is'} open in the timeline."
                             + (" An export is running." if self.encoding else ""),
                             ["Save", "Close without saving", "Cancel"], default=0, cancel=2)
            self.wait_window(d)
            if d.result not in (0, 1):
                return
            if d.result == 0 and not self.save_project():
                return
        try:
            self.save_all_settings()
            self._save_session()
            self.stop_playback()
            cleanup_wavs()
        finally:
            self.destroy()

    # ------------------------------------------------------------- export
    def start_export(self):
        if self.encoding:
            return
        if not self.rows:
            messagebox.showinfo("No clips", "Add at least one video first.")
            return
        br_txt = self.bitrate_var.get().strip()
        bitrate = None
        target_mb = None
        if br_txt:
            bitrate = self._bitrate()
            if bitrate is None:
                messagebox.showerror("Invalid bitrate", "Enter the bitrate in Mbps, e.g. 20 (or clear the field "
                                                        "to use the file size limit).")
                return
        else:
            try:
                target_mb = float(self.size_var.get().replace(",", "."))
                assert target_mb > 0
            except Exception:
                messagebox.showerror("Invalid size", "Enter the max file size in MB, e.g. 50")
                return
        try:
            tlen = float(self.tlen_var.get().replace(",", "."))
        except ValueError:
            tlen = 0.4
        clips = [r.clip() for r in self.rows]
        if self.mutemic_var.get():                          # drop every audio track except the first (game audio)
            for c in clips:
                c["naudio"] = min(c.get("naudio", 1), 1)
        inputs = {os.path.abspath(c["path"]).lower() for c in clips}
        join = False
        if len(clips) > 1:
            join = self.ask_export_mode(len(clips))
            if join is None:
                return

        if join:
            stem = os.path.splitext(os.path.basename(clips[0]["path"]))[0]
            out = self._auto_path(self._init_dir(None), stem, inputs) if self._init_dir(None) else \
                filedialog.asksaveasfilename(title="Save joined video as", defaultextension=".mp4",
                                             initialfile=f"{self._out_stem(stem)}.mp4",
                                             initialdir=os.path.dirname(clips[0]["path"]),
                                             filetypes=[("MP4 video", "*.mp4")])
            if not out:
                return
            if os.path.abspath(out).lower() in inputs:
                messagebox.showerror("Same file", "Choose a different output file name.")
                return
            jobs = [(out, clips, None)]
        elif len(clips) == 1:
            stem = os.path.splitext(os.path.basename(clips[0]["path"]))[0]
            out = self._auto_path(self._init_dir(None), stem, inputs) if self._init_dir(None) else \
                filedialog.asksaveasfilename(title="Save as", defaultextension=".mp4",
                                             initialfile=f"{self._out_stem(stem)}.mp4",
                                             initialdir=os.path.dirname(clips[0]["path"]),
                                             filetypes=[("MP4 video", "*.mp4")])
            if not out:
                return
            if os.path.abspath(out).lower() in inputs:
                messagebox.showerror("Same file", "Choose a different output file name.")
                return
            jobs = [(out, clips, self._clip_override(self.rows[0]))]
        else:
            folder = self._init_dir(None) or filedialog.askdirectory(
                title="Choose a folder for the exported files", initialdir=os.path.dirname(clips[0]["path"]))
            if not folder:
                return
            jobs, used = [], set()
            for r, c in zip(self.rows, clips):
                base = os.path.join(folder, self._out_stem(os.path.splitext(os.path.basename(c["path"]))[0]))
                p, i = base + ".mp4", 1
                while os.path.exists(p) or p.lower() in used:
                    i += 1
                    p = f"{base} ({i}).mp4"
                used.add(p.lower())
                jobs.append((p, [c], self._clip_override(r)))

        st = {"target_mb": target_mb, "bitrate_mbps": bitrate, "res_mode": self.res_var.get(),
              "codec": codec_key(self.codec_var.get()),
              "audio_k": self.audio_var.get(), "ratio": self._ratio(),
              "xfade": TRANSITIONS.get(self.trans_var.get()) if join else None, "T": tlen,
              "tonemap": self.hdr_var.get(), "fast_audio": self.fast_aud_var.get(), "slow_audio": self.slow_aud_var.get(), "slow_pitch": self.pitch_var.get(), "fast_pitch": self.fast_pitch_var.get(), "vhs": self.vhs_var.get(), "slow_interp": self.interp_var.get(),
              "fast_vol": int(self.fast_vol_var.get()), "slow_vol": int(self.slow_vol_var.get()),
              "zoom_fx": self.zoom_fx_var.get(), "zoom_snd": self.zoom_snd_var.get(),
              "zoom_len": self.zoom_len_var.get().replace(",", "."), "zoom_vol": int(self.zoom_vol_var.get()),
              "mix_audio": self.atrk_var.get().startswith("Mix")}

        self.stop_playback()
        self.encoding = True
        self.encoder = Encoder(self.set_progress, self.set_export_msg)
        self._go_state(True)
        self.open_btn.pack_forget()
        self.prog.set_done(False)
        self.prog["value"] = 0
        self.exp_msg.config(text="Starting...")
        threading.Thread(target=self._export_worker, args=(jobs, st), daemon=True).start()

    def ask_export_mode(self, n):
        """Pop-up before an export of several clips. True = join into one video, False = separate files,
        None = cancelled. The last choice is the default button."""
        d = ChoiceDialog(self, "Export", f"{n} clips are in the timeline. How do you want to export them?",
                         ["Join into one video", "Each clip as its own file", "Cancel"],
                         default=0 if self.mode_var.get() == "join" else 1, cancel=2)
        self.wait_window(d)
        if d.result in (0, 1):
            self.mode_var.set("join" if d.result == 0 else "separate")
            return d.result == 0
        return None

    def _clip_override(self, row):
        """Settings that replace the global ones for one separately exported clip (its bitrate preset)."""
        p = self.preset_by_name(row.preset)
        if not p:
            return None
        res = self.res_var.get()
        return {"bitrate_mbps": p["mbps"], "target_mb": None, "res_mode": "Original" if res == "Auto" else res}

    def _export_done(self, path, failed, took=""):
        try:
            if winsound is not None:
                winsound.MessageBeep(winsound.MB_ICONEXCLAMATION if failed else winsound.MB_ICONASTERISK)
        except Exception:
            pass
        if path:
            self.prog.config(value=100)
            self.prog.set_done(True, f"Done in {took}" if took else None)
        else:
            self.prog.set_failed(f"Failed after {took}" if took else "Failed")
        if path:
            self.out_path = path
            self.open_btn.pack(side="left", padx=(8, 0))

    def open_output_folder(self):
        p = getattr(self, "out_path", None)
        if not p:
            return
        try:
            if os.name == "nt":
                if os.path.exists(p):
                    subprocess.Popen(["explorer", "/select,", os.path.normpath(p)])
                else:
                    os.startfile(os.path.dirname(p))
        except Exception as e:
            self.set_status(f"Could not open the folder: {e}")

    def cancel(self):
        if self.encoder:
            self.encoder.cancel()

    def _export_worker(self, jobs, st):
        enc = self.encoder
        done, errors = [], []
        current = None
        t_start = time.time()
        try:
            for k, (out, clips, over) in enumerate(jobs):
                current = out
                tag = f"[{k + 1}/{len(jobs)}] " if len(jobs) > 1 else ""
                try:
                    size = enc.encode(out, clips, dict(st, **over) if over else st, pbase=100.0 * k / len(jobs),
                                      pspan=100.0 / len(jobs), tag=tag)
                    done.append((out, size))
                except Cancelled:
                    raise
                except Exception as e:
                    errors.append(f"{os.path.basename(out)}: {e}")
                    try:
                        os.remove(out)
                    except OSError:
                        pass
            lines = [f"{os.path.basename(o)}  -  {s / 1048576:.2f} MB" for o, s in done]
            limit = ("per-clip bitrates" if any(j[2] for j in jobs) else
                     f"video bitrate {st['bitrate_mbps']:g} Mbps" if st.get("bitrate_mbps")
                     else f"limit {st['target_mb']:g} MB each")
            msg = (f"Saved {len(done)} file(s) ({limit}):\n\n" + "\n".join(lines)
                   if done else "")
            if errors:
                msg += ("\n\n" if msg else "") + "Problems:\n" + "\n".join(errors)
            info = ", ".join(f"{os.path.basename(o)} ({sz / 1048576:.2f} MB)" for o, sz in done[:3])
            if len(done) > 3:
                info += f" + {len(done) - 3} more"
            took = fmt_elapsed(time.time() - t_start)
            self.set_export_msg((f"{len(done)} saved ({limit}): {info}" if done else "Nothing was saved")
                                + (f"  |  {len(errors)} failed: {errors[0]}" if errors else ""))
            self.after(0, lambda: self._export_done(done[-1][0] if done else None, bool(errors), took))
        except Cancelled:
            self.set_export_msg("The export was stopped - the unfinished file was deleted")
            self.after(0, lambda: self.prog.set_failed("Aborted"))
            if current:                      # remove the half-written file
                try:
                    os.remove(current)
                except OSError:
                    pass
        except Exception as e:
            m = str(e)
            self.set_export_msg("Export failed - " + (m.strip().splitlines() or ["unknown error"])[0][:160])
            self.after(0, lambda: self.prog.set_failed("Failed"))
            self.after(0, lambda: messagebox.showerror("Export failed", m))
        finally:
            self.encoding = False
            self.after(0, lambda: self._go_state(False))


if __name__ == "__main__":
    app = App()
    if _LOG:
        def _report(exc, val, tb):
            traceback.print_exception(exc, val, tb, file=_LOG)
        app.report_callback_exception = _report
    app.mainloop()
