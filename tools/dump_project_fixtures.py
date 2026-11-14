"""Phase 2 fixtures: project loading and the app's export settings, from the Python rules in reference_export.py.

    ..\\Dev\\library\\python\\python.exe -I tools\\dump_project_fixtures.py

Uses the sample files in tests/fixtures/files. File existence and ffprobe are faked: every path in a project
"exists" (except names containing "missing"), and each file probes as a 20 s 1080p60 clip with 2 audio tracks.
Writes tests/fixtures/projects.json.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reference_export as rx                       # noqa: E402

m, dfx = rx.m, rx.dfx
FILES = os.path.join(dfx.OUT, "files")
FAKE_INFO = {"w": 1920, "h": 1080, "fps": 60.0, "dur": 20.0, "audio": True, "naudio": 2, "bitrate": 9_000_000.0,
             "size": 22_500_000, "trc": None, "prim": "bt709", "space": "bt709"}


class Var:
    def __init__(self, v):
        self.v = v

    def get(self):
        return self.v


def project_data_cases():
    """App.project_data (what Save project writes) on stand-in rows and settings: rounding, sorting, relative paths."""
    import types
    secs = [{"a": 9.0625, "b": 11.0005, "speed": 0.5}, {"a": 2.12345, "b": 5.0, "speed": 4.0}]     # unsorted, ties
    zooms = [{"a": 3.0, "b": 7.00049, "x": 0.12345, "y": 0.00005, "w": 0.40625}]
    rows = [
        types.SimpleNamespace(path="C:\\clips\\a.mp4", start=1.0625, end=18.2499, preset="Discord", sections=secs, zooms=zooms),
        types.SimpleNamespace(path="C:\\clips\\sub\\b.mkv", start=0.0, end=9.0, preset=None, sections=[], zooms=[]),
        types.SimpleNamespace(path="D:\\other\\c.mov", start=0.33333, end=2.0, preset=None, sections=[], zooms=[]),
        types.SimpleNamespace(path="C:/clips/fwd slash/d.mp4", start=0.0005, end=1.0015, preset=None, sections=[], zooms=[]),
    ]
    st = {"size_var": "50", "bitrate_var": "", "res_var": "Auto", "codec_var": "HEVC (hevc_nvenc)", "crop_var": "16:9",
          "audio_var": "128k", "atrk_var": "Keep separate tracks", "mutemic_var": True, "fast_aud_var": "Mute",
          "slow_aud_var": "Slow-mo effect with sound", "pitch_var": "Stays the same (stretched)",
          "fast_pitch_var": "Speeds up with the video (high)", "vhs_var": True, "fast_vol_var": 70, "slow_vol_var": 150,
          "zoom_fx_var": "Zoom motion", "zoom_snd_var": "Mute", "zoom_len_var": "0,6", "zoom_vol_var": 80,
          "interp_var": "Frame blending", "hdr_var": False, "mode_var": "join", "trans_var": "Distance", "tlen_var": "0.5"}
    out = []
    for proj in (None, "C:\\clips\\my project.clipproj", "C:\\clips\\deeper\\x.clipproj", "E:\\p.clipproj"):
        for active in (0, 2):
            fake = types.SimpleNamespace(rows=rows, active_row=rows[active], project_path=proj,
                                         **{k: Var(v) for k, v in st.items()})
            out.append({"project_path": proj, "active": active,
                        "rows": [{"path": r.path, "start": r.start, "end": r.end, "preset": r.preset,
                                  "sections": r.sections, "zooms": r.zooms} for r in rows],
                        "state": {k.replace("_var", ""): v for k, v in st.items()},
                        "result": m.App.project_data(fake)})
    return out


def main():
    real_isfile, real_probe = os.path.isfile, m.probe
    os.path.isfile = lambda p: isinstance(p, str) and (p.lower().endswith(m.PROJECT_EXT) or p.lower().endswith(".json")
                                                       or (p != "" and "missing" not in p.lower()))
    m.probe = lambda p: dict(FAKE_INFO)
    m.PRESETS_FILE = os.path.join(FILES, "presets.json")
    presets = m.load_presets()
    settings = json.load(open(os.path.join(FILES, "settings.json"), encoding="utf-8"))
    out = {"fake_info": FAKE_INFO, "presets_file": "presets.json", "settings_file": "settings.json",
           "defaults_no_settings": rx.saved_state({}), "defaults_from_settings": rx.saved_state(settings), "projects": []}
    try:
        for name in sorted(f for f in os.listdir(FILES) if f.endswith(m.PROJECT_EXT)):
            rows, pst = rx.load_project(os.path.join(FILES, name), presets)
            state = rx.saved_state(settings)
            rx.apply_project_settings(state, pst)
            out["projects"].append({
                "file": name,
                "rows": [{k: r[k] for k in ("path", "start", "end", "preset", "sections", "zooms")} for r in rows],
                "clips": [dfx.jsonable(rx.row_clip(r)) for r in rows],
                "state": state,
            })
    finally:
        os.path.isfile, m.probe = real_isfile, real_probe
    out["project_data"] = project_data_cases()
    with open(os.path.join(dfx.OUT, "projects.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, indent=1, allow_nan=False)
        f.write("\n")
    print("projects.json:", [p["file"] for p in out["projects"]])


if __name__ == "__main__":
    main()
