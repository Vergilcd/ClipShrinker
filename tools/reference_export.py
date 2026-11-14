"""Phase 2 check: export a .clipproj with the frozen Python 1.0.8 Encoder and real ffmpeg, logging every command.

    ..\\Dev\\library\\python\\python.exe -I tools\\reference_export.py <project> --out <file|folder> [--join|--separate]
        [--settings settings.json] [--presets presets.json] [--tools dir] [--log-commands file.json]

The Tk App cannot run headless, so its export path (settings defaults, _apply_project_settings, _load_clips,
start_export, _export_worker) is restated here, line for line, from the reference. The Encoder, probe and all filter
code are the reference module itself. The log has the same format as the C# CLI's --log-commands.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dump_fixtures as dfx                          # noqa: E402  (loads the frozen reference as dfx.m)

m = dfx.m


def saved_state(settings):
    """App.__init__ defaults from settings.json (_saved / _saved_int), then _bitrate_changed()."""
    s = settings or {}

    def saved(key, default, allowed=None):
        v = s.get(key, default)
        if isinstance(default, bool):
            return v if isinstance(v, bool) else default
        if not isinstance(v, str):
            return default
        return v if (allowed is None or v in allowed) else default

    def saved_int(key, default):
        v = s.get(key, default)
        return max(0, min(200, v)) if isinstance(v, int) and not isinstance(v, bool) else default

    st = {
        "hdr": saved("hdr", True), "fast_audio": saved("fast_audio", m.FAST_AUDIO[0], m.FAST_AUDIO),
        "slow_audio": saved("slow_audio", m.SLOW_AUDIO[0], m.SLOW_AUDIO), "fast_vol": saved_int("fast_vol", 70),
        "slow_vol": saved_int("slow_vol", 150), "zoom_fx": saved("zoom_fx", m.ZOOM_FX[0], m.ZOOM_FX),
        "zoom_snd": saved("zoom_snd", m.ZOOM_AUDIO[0], m.ZOOM_AUDIO), "zoom_len": str(s.get("zoom_len", "0.4")),
        "zoom_vol": saved_int("zoom_vol", 80), "slow_pitch": saved("slow_pitch", m.SLOW_PITCH[0], m.SLOW_PITCH),
        "fast_pitch": saved("fast_pitch", m.FAST_PITCH[1], m.FAST_PITCH), "vhs": saved("vhs", True),
        "slow_interp": saved("slow_interp", m.SLOW_INTERP[2], m.SLOW_INTERP),
        "atrk": saved("atrk", "Mix into one track", ["Keep separate tracks", "Mix into one track"]),
        "mute_mic": saved("mute_mic", False), "size": saved("size", "50"),
        "crop": saved("crop", "16:9", list(m.ASPECTS)), "res": saved("res", "Auto", m.RES_SIZE_MODE + m.RES_BITRATE_MODE),
        "codec": saved("codec", "HEVC (hevc_nvenc)", m.CODECS), "bitrate": saved("bitrate", ""),
        "res_before": saved("res_before_bitrate", "Auto", m.RES_SIZE_MODE),
        "audio": saved("audio", "128k", ["48k", "64k", "96k", "128k", "192k"]), "mode": saved("mode", "join", ["separate", "join"]),
        "trans": saved("trans", "Gradient wipe, left to right", list(m.TRANSITIONS)), "tlen": saved("tlen", "0.4"),
        "prefix": saved("prefix", ""), "suffix": saved("suffix", ""),
    }
    bitrate_changed(st)
    return st


def bitrate_value(st):
    try:
        v = float(st["bitrate"].strip().replace(",", "."))
    except ValueError:
        return None
    return v if v > 0 else None


def bitrate_changed(st):
    if bitrate_value(st) is not None:
        if st["res"] not in m.RES_BITRATE_MODE:
            st["res_before"] = st["res"]
            st["res"] = "Original"
    elif st["res"] not in m.RES_SIZE_MODE:
        st["res"] = st["res_before"] if st["res_before"] in m.RES_SIZE_MODE else "Auto"


def apply_project_settings(state, st):
    if not isinstance(st, dict):
        return

    def put(key, allowed=None, skey=None):
        v = st.get(key)
        if isinstance(v, str) and (allowed is None or v in allowed):
            state[skey or key] = v
            return True
        return False
    put("size")
    if put("bitrate"):
        bitrate_changed(state)
    put("res", list(m.RES_SIZE_MODE) + list(m.RES_BITRATE_MODE))
    put("codec", m.CODECS)
    put("crop", list(m.ASPECTS))
    put("audio", ["48k", "64k", "96k", "128k", "192k"])
    put("atrk", ["Keep separate tracks", "Mix into one track"])
    put("mode", ["separate", "join"])
    put("trans", list(m.TRANSITIONS))
    put("tlen")
    put("fast_audio", m.FAST_AUDIO)
    put("slow_audio", m.SLOW_AUDIO)
    put("slow_pitch", m.SLOW_PITCH)
    put("fast_pitch", m.FAST_PITCH)
    put("zoom_fx", m.ZOOM_FX)
    put("zoom_snd", m.ZOOM_AUDIO)
    if isinstance(st.get("zoom_len"), (str, int, float)) and not isinstance(st.get("zoom_len"), bool):
        state["zoom_len"] = str(st["zoom_len"])
    if isinstance(st.get("zoom_vol"), int) and not isinstance(st.get("zoom_vol"), bool):
        state["zoom_vol"] = max(0, min(200, st["zoom_vol"]))
    for k in ("fast_vol", "slow_vol"):
        if isinstance(st.get(k), int) and not isinstance(st.get(k), bool):
            state[k] = max(0, min(200, st[k]))
    put("slow_interp", m.SLOW_INTERP)
    for key in ("mute_mic", "vhs", "hdr"):
        if isinstance(st.get(key), bool):
            state[key] = st[key]
    bitrate_changed(state)


def load_project(path, presets):
    """load_project + _load_clips + add_files: returns rows as dicts (path, info, start, end, preset, sections, zooms)."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    base = os.path.dirname(os.path.abspath(path))
    rows = []
    for c in data["clips"]:
        p = c.get("path", "") if isinstance(c, dict) else ""
        if not os.path.isfile(p) and isinstance(c, dict) and c.get("rel"):
            alt = os.path.normpath(os.path.join(base, c["rel"]))
            p = alt if os.path.isfile(alt) else p
        if not os.path.isfile(p):
            print("missing:", c.get("path") if isinstance(c, dict) else "?", file=sys.stderr)
            continue
        if not p.lower().endswith(m.VIDEO_EXTS):
            continue
        info = m.probe(p)
        dur = info["dur"]
        row = {"path": p, "info": info, "start": 0.0, "end": dur, "preset": None, "sections": [], "zooms": []}
        try:
            a, b = float(c.get("start", 0)), float(c.get("end", dur))
            if 0 <= a < b <= dur + 0.01:
                row["start"], row["end"] = a, min(b, dur)
            if isinstance(c.get("preset"), str):
                row["preset"] = c["preset"] if any(x["name"] == c["preset"] for x in presets) else None
            for sd in c.get("sections", []):
                sa, sb, sp = float(sd["a"]), float(sd["b"]), m.clamp_speed(float(sd["speed"]))
                if 0 <= sa < sb <= dur + 0.01:
                    row["sections"].append({"a": sa, "b": min(sb, dur), "speed": sp})
            for zd in c.get("zooms", []):
                za, zb, zw = float(zd["a"]), float(zd["b"]), float(zd["w"])
                zw = min(1.0, max(m.ZOOM_MIN_W, zw))
                zx, zy = min(max(float(zd["x"]), 0.0), 1 - zw), min(max(float(zd["y"]), 0.0), 1 - zw)
                if 0 <= za < zb <= dur + 0.01:
                    row["zooms"].append({"a": za, "b": min(zb, dur), "x": zx, "y": zy, "w": zw})
        except (TypeError, ValueError, KeyError):
            pass
        rows.append(row)
    return rows, data.get("settings")


def row_clip(r):
    """ClipRow.clip()."""
    i = r["info"]
    c = dfx.clip(path=r["path"], start=r["start"], end=r["end"], w=i["w"], h=i["h"], fps=i["fps"], naudio=i["naudio"],
                 bitrate=i["bitrate"], trc=i["trc"], prim=i["prim"], space=i["space"], sections=r["sections"],
                 zooms=r["zooms"])
    c["audio"] = i["audio"]
    return c


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("project")
    ap.add_argument("--out", required=True)
    ap.add_argument("--join", action="store_true")
    ap.add_argument("--separate", action="store_true")
    ap.add_argument("--settings")
    ap.add_argument("--presets")
    ap.add_argument("--tools", default=os.path.join(dfx.ROOT, "..", "Dev", "library"))
    ap.add_argument("--log-commands")
    a = ap.parse_args()

    tools = os.path.abspath(a.tools)
    m.FFMPEG, m.FFPROBE = os.path.join(tools, "ffmpeg.exe"), os.path.join(tools, "ffprobe.exe")
    m.RIFE_DIR = os.path.join(tools, "rife")
    settings = json.load(open(a.settings, encoding="utf-8")) if a.settings else {}
    state = saved_state(settings)
    presets = []
    if a.presets:
        m.PRESETS_FILE = a.presets
        presets = m.load_presets()
    rows, pst = load_project(a.project, presets)
    apply_project_settings(state, pst)

    # start_export
    clips = [row_clip(r) for r in rows]
    if state["mute_mic"]:
        for c in clips:
            c["naudio"] = min(c.get("naudio", 1), 1)
    inputs = {os.path.abspath(c["path"]).lower() for c in clips}
    join = False
    if len(clips) > 1:
        join = a.join or (not a.separate and state["mode"] == "join")
    br_txt = state["bitrate"].strip()
    bitrate = target_mb = None
    if br_txt:
        bitrate = bitrate_value(state)
        if bitrate is None:
            sys.exit("Invalid bitrate")
    else:
        target_mb = float(state["size"].replace(",", "."))
    try:
        tlen = float(state["tlen"].replace(",", "."))
    except ValueError:
        tlen = 0.4

    def out_stem(stem):
        pre = m.clean_suffix(state["prefix"])
        tag = m.clean_suffix(state["suffix"]) or ("HEVC" if "HEVC" in state["codec"] else "H264")
        return (f"{pre}_" if pre else "") + f"{stem}_{tag}"

    def auto_path(folder, stem, taken=()):
        base = os.path.join(folder, out_stem(stem))
        p, i = base + ".mp4", 1
        while os.path.exists(p) or p.lower() in taken:
            i += 1
            p = f"{base} ({i}).mp4"
        return p

    def override(r):
        p = next((x for x in presets if x["name"] == r["preset"]), None)
        if not p:
            return None
        res = state["res"]
        return {"bitrate_mbps": p["mbps"], "target_mb": None, "res_mode": "Original" if res == "Auto" else res}

    stem0 = os.path.splitext(os.path.basename(clips[0]["path"]))[0]
    if join or len(clips) == 1:
        out = auto_path(a.out, stem0, inputs) if os.path.isdir(a.out) else os.path.abspath(a.out)
        jobs = [(out, clips, None if join else override(rows[0]))]
    else:
        os.makedirs(a.out, exist_ok=True)
        jobs, used = [], set()
        for r, c in zip(rows, clips):
            p = auto_path(a.out, os.path.splitext(os.path.basename(c["path"]))[0], used)
            used.add(p.lower())
            jobs.append((p, [c], override(r)))
    st = {"target_mb": target_mb, "bitrate_mbps": bitrate, "res_mode": state["res"], "codec": m.codec_key(state["codec"]),
          "audio_k": state["audio"], "ratio": m.ASPECTS.get(state["crop"]),
          "xfade": m.TRANSITIONS.get(state["trans"]) if join else None, "T": tlen,
          "tonemap": state["hdr"], "fast_audio": state["fast_audio"], "slow_audio": state["slow_audio"],
          "slow_pitch": state["slow_pitch"], "fast_pitch": state["fast_pitch"], "vhs": state["vhs"],
          "slow_interp": state["slow_interp"], "fast_vol": int(state["fast_vol"]), "slow_vol": int(state["slow_vol"]),
          "zoom_fx": state["zoom_fx"], "zoom_snd": state["zoom_snd"], "zoom_len": state["zoom_len"].replace(",", "."),
          "zoom_vol": int(state["zoom_vol"]), "mix_audio": state["atrk"].startswith("Mix")}

    # _export_worker, with every started process logged
    started = []
    real_popen = m.subprocess.Popen

    def logging_popen(cmd, *args, **kw):
        started.append(list(cmd))
        return real_popen(cmd, *args, **kw)
    m.subprocess.Popen = logging_popen
    enc = m.Encoder(lambda pct: None, lambda text: print(text, file=sys.stderr))
    log, done, errors = [], [], []
    for k, (out, cl, over) in enumerate(jobs):
        tag = f"[{k + 1}/{len(jobs)}] " if len(jobs) > 1 else ""
        first = len(started)
        try:
            size = enc.encode(out, cl, dict(st, **over) if over else st, pbase=100.0 * k / len(jobs),
                              pspan=100.0 / len(jobs), tag=tag)
            done.append((out, size))
        except Exception as e:
            errors.append(f"{os.path.basename(out)}: {e}")
        log.append({"out": out, "commands": started[first:]})
    for o, s in done:
        print(f"{os.path.basename(o)}  -  {s / 1048576:.2f} MB")
    for e in errors:
        print("Problem:", e)
    if a.log_commands:
        with open(a.log_commands, "w", encoding="utf-8") as f:
            json.dump({"jobs": log}, f, indent=1)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
