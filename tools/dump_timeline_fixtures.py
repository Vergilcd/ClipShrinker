"""Phase 4 fixtures: the 1.0.8 ClipRow editing logic driven by scripted and random mouse / key sequences.

    ..\\Dev\\library\\python\\python.exe -I tools\\dump_timeline_fixtures.py

ClipRow is a Tk widget, but its editing methods only need a few things from Tk (canvas width, focus, text
variables). FakeRow provides those and borrows the real methods from the frozen reference, so on_down / on_move /
add_section / set_trim ... run unchanged. After every step the state is recorded; the C# TimelineEditor replays the
same steps and must end up in the same state. Writes tests/fixtures/timeline.json.
"""
import json
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dump_fixtures as dfx                       # noqa: E402

m = dfx.m
CR = m.ClipRow


class Var:
    def __init__(self, v=""):
        self.v = v

    def get(self):
        return self.v

    def set(self, v):
        self.v = v


class Canvas:
    def __init__(self, w):
        self.w = w

    def winfo_width(self):
        return self.w

    def focus_set(self):
        pass

    def focus_get(self):
        return None


class Label:
    def config(self, **kw):
        pass


class FakeApp:
    def __init__(self):
        self.selection = set()
        self.status, self.undo, self.previews = [], [], []
        self.playing = False

    def set_status(self, t):
        self.status.append(t)

    def push_undo(self, label):
        self.undo.append(label)

    def request_preview(self, row, t):
        self.previews.append(t)

    def stop_playback(self):
        pass

    def bracket_refresh(self):
        pass

    def _renumber(self):
        pass

    def set_active(self, row):
        row.active = True
        row._layout()
        self.selection = {row}

    def click_row(self, row, state=0):
        self.set_active(row)


class FakeRow:
    """Just enough of ClipRow for its editing methods; the methods themselves are the reference's."""
    for _n in ("_x", "_t", "set_view", "zoom_at", "fit", "on_wheel", "vis", "vis_z", "set_trim", "_tent_commit",
               "_zneighbors", "_neighbors", "add_zoom", "remove_zoom", "add_section", "remove_section",
               "_entry_commit", "on_right", "on_down", "_snap_setup", "_snap", "on_move", "zoom_factor", "level",
               "edited_len"):
        locals()[_n] = getattr(CR, _n)
    SNAP_PX = CR.SNAP_PX

    def __init__(self, W, info, active=True):
        self.app = FakeApp()
        self.canvas = Canvas(W)
        self.info = info
        self.start, self.end, self.playhead = 0.0, info["dur"], 0.0
        self.sections, self.zooms = [], []
        self.sel = self.zsel = None
        self.view_a, self.view_len = 0.0, max(info["dur"], 0.1)
        self.drag = None
        self.zoom_lbl = Label()
        self._thumb_job = None
        self.tvars = {"start": Var(), "end": Var()}
        self.active = active
        self._layout()

    def _layout(self):
        if self.active:
            self.lane_h, self.tlb = m.LANE_H + m.ZLANE_H, m.TL_H
        else:
            self.lane_h, self.tlb = m.COMPACT_LANE_H, m.COMPACT_TL_H

    def after(self, ms, fn, *a):
        return None

    def redraw(self, total=True):
        pass

    def refresh_thumbs(self):
        pass

    def new_section(self, a, b, speed):
        return {"a": a, "b": b, "speed": speed, "var": Var(f"{speed:g}x")}


DRAG_NAMES = {"play": "Play", "start": "Start", "end": "End", "sa": "SecA", "sb": "SecB", "move": "SecMove",
              "za": "ZoomA", "zb": "ZoomB", "zmove": "ZoomMove", "ov": "Overview"}


def drag_name(d):
    if d is None:
        return "None"
    return DRAG_NAMES[d if isinstance(d, str) else d[0]]


def state(r):
    secs, zs = r.sections, r.zooms
    return {
        "start": r.start, "end": r.end, "playhead": r.playhead, "view_a": r.view_a, "view_len": r.view_len,
        "active": r.active, "drag": drag_name(r.drag),
        "sections": [[s["a"], s["b"], s["speed"]] for s in secs],
        "zooms": [[z["a"], z["b"], z["x"], z["y"], z["w"]] for z in zs],
        "sel": next((i for i, s in enumerate(secs) if s is r.sel), None),
        "zsel": next((i for i, z in enumerate(zs) if z is r.zsel), None),
        "edited_len": r.edited_len(), "level": r.level(),
        "status": list(r.app.status), "undo": list(r.app.undo), "previews": len(r.app.previews),
    }


def ev(x=0, y=0, ctrl=False, delta=0):
    class E:
        pass
    e = E()
    e.x, e.y, e.state, e.delta = x, y, (0x4 if ctrl else 0), delta
    return e


def apply(r, op):
    """One step. op = [name, args...] (the same list goes into the fixture for the C# replay)."""
    k = op[0]
    r.app.status.clear()
    r.app.undo.clear()
    r.app.previews.clear()
    if k == "down":
        _, x, y = op
        r.on_down(ev(x, y))                    # (on a compact strip on_down activates the row itself, then seeks)
    elif k == "move":
        r.on_move(ev(op[1], 0))
    elif k == "up":
        r.drag = None
    elif k == "right":
        r.on_right(ev(op[1], op[2]))
    elif k == "wheel":
        _, x, steps, ctrl = op
        r.on_wheel(ev(x, 0, ctrl, int(steps * 120)))
    elif k == "add":
        r.add_section(op[1])
    elif k == "zoom":
        r.add_zoom()
    elif k == "trim":
        r.set_trim(op[1])
    elif k == "trimtext":
        _, which, text = op
        r.tvars[which].set(text)
        r._tent_commit(which, True)
    elif k == "speedtext":
        _, i, text = op
        sec = r.sections[i]
        sec["var"].set(text)
        r._entry_commit(sec, True)
    elif k == "fit":
        r.fit()
    elif k == "deactivate":
        r.active = False
        r._layout()
    else:
        raise ValueError(k)


def run(name, W, dur, ops, w=1920, h=1080):
    info = {"w": w, "h": h, "dur": dur, "fps": 60.0}
    r = FakeRow(W, info)
    steps = []
    for op in ops:
        apply(r, op)
        steps.append({"op": op, "state": state(r)})
    return {"name": name, "W": W, "dur": dur, "w": w, "h": h, "steps": steps}


def X(W, dur, t):
    return m.PAD + (W - 2 * m.PAD) * t / dur


def scripted():
    W, D = 1000, 20.0
    x = lambda t: X(W, D, t)
    LANE, ZL, STRIP = 15, m.LANE_H + 5, 80
    cases = []
    cases.append(run("seek and trim handles", W, D, [
        ["down", x(5), STRIP], ["move", x(7.3)], ["up"],
        ["down", x(0) + 3, STRIP], ["move", x(2.5)], ["move", x(19.8)], ["up"],
        ["down", x(20) - 2, STRIP], ["move", x(15)], ["move", x(1)], ["up"],
        ["down", x(10), LANE], ["move", x(10)], ["up"],
    ]))
    cases.append(run("hotkeys: trim and sections", W, D, [
        ["down", x(4), STRIP], ["up"], ["trim", "start"], ["down", x(3.8), STRIP], ["up"], ["trim", "end"],
        ["down", x(16), STRIP], ["up"], ["trim", "end"], ["down", x(15.8), STRIP], ["up"], ["trim", "start"],
        ["down", x(6), STRIP], ["up"], ["add", 2.0], ["add", 2.0], ["add", 0.5],
        ["down", x(6.2), STRIP], ["up"], ["add", 3.0], ["add", 3.0], ["add", 3.0], ["add", 3.0], ["add", 3.0],
        ["add", 3.0], ["add", 3.0], ["add", 3.0], ["add", 3.0], ["add", 3.0], ["add", 3.0], ["add", 3.0],
    ]))
    cases.append(run("dragging sections and snapping", W, D, [
        ["down", x(3), STRIP], ["up"], ["add", 2.0], ["down", x(9), STRIP], ["up"], ["add", 0.5],
        ["down", x(3.5), LANE], ["move", x(5)], ["move", x(8.4)], ["move", x(9.1)], ["up"],
        ["down", x(12.4), STRIP], ["up"],
        ["down", x(9), STRIP], ["move", x(12.35)], ["up"],
        ["down", x(10), STRIP], ["move", x(12.32)], ["move", x(0.5)], ["up"],
        ["down", x(9.5), LANE], ["move", x(15.0)], ["up"],
        ["right", x(3.5), STRIP], ["right", x(18), STRIP], ["right", x(15.5), LANE],
    ]))
    cases.append(run("speed boxes", W, D, [
        ["add", 2.0], ["speedtext", 0, "4"], ["speedtext", 0, " 0,25x "], ["speedtext", 0, "abc"],
        ["speedtext", 0, "-1"], ["speedtext", 0, "500"], ["speedtext", 0, "1.5×"], ["speedtext", 0, "0"],
    ]))
    cases.append(run("trim boxes", W, D, [
        ["trimtext", "start", "0:03.5"], ["trimtext", "start", "abc"], ["trimtext", "end", "100"],
        ["trimtext", "end", "0:04"], ["trimtext", "start", "1:00"], ["trimtext", "start", "2,25"],
        ["trimtext", "end", "17;5"], ["trimtext", "end", "0:17.5"],
    ]))
    cases.append(run("zoom sections", W, D, [
        ["down", x(2), STRIP], ["up"], ["zoom"], ["zoom"], ["zoom"],
        ["down", x(3), ZL], ["move", x(5.5)], ["up"],
        ["down", x(5.5), ZL], ["move", x(7)], ["up"],
        ["down", x(9.9), ZL], ["move", x(8)], ["up"],
        ["down", x(15), ZL], ["up"],
        ["down", x(4), STRIP], ["up"],
        ["right", x(10), ZL], ["right", x(1), ZL],
    ]))
    cases.append(run("timeline zoom, scroll, overview, edge scroll", W, D, [
        ["wheel", x(5), 2, True], ["wheel", x(5), 1, False], ["wheel", x(5), -3, False],
        ["wheel", 300, 6, True], ["wheel", 300, 6, True], ["wheel", 700, -1, True],
        ["down", 500, m.TL_H - 4], ["move", 800], ["move", 20], ["up"],
        ["down", 500, STRIP], ["move", 5], ["move", 2], ["move", 995], ["up"],
        ["fit"], ["wheel", x(10), -2, True], ["wheel", x(10), 0.5, False],
    ]))
    cases.append(run("compact strip click", W, D, [["deactivate"], ["down", x(12), 20], ["move", x(13)], ["up"]]))
    cases.append(run("ultrawide thumbnails, short clip", 640, 3.0, [
        ["add", 2.0], ["zoom"], ["wheel", 320, 3, True], ["down", 320, STRIP], ["up"], ["trim", "end"],
    ], w=3440, h=1440))
    return cases


def random_walk(seed, n=400):
    rnd = random.Random(seed)
    W, D = rnd.choice([(1000, 20.0), (1600, 63.5), (700, 8.0)])
    ys = [10, 20, 35, 45, 80, 120, m.TL_H - 4]
    ops = []
    held = False
    for _ in range(n):
        p = rnd.random()
        if held:
            if p < 0.75:
                ops.append(["move", round(rnd.uniform(-20, W + 20), 1)])
            else:
                ops.append(["up"])
                held = False
            continue
        if p < 0.35:
            ops.append(["down", round(rnd.uniform(0, W), 1), rnd.choice(ys)])
            held = True
        elif p < 0.45:
            ops.append(["add", rnd.choice([2.0, 0.5, 4.0])])
        elif p < 0.5:
            ops.append(["zoom"])
        elif p < 0.6:
            ops.append(["wheel", round(rnd.uniform(0, W), 1), rnd.choice([-2, -1, 1, 2, 3]), rnd.random() < 0.6])
        elif p < 0.68:
            ops.append(["right", round(rnd.uniform(0, W), 1), rnd.choice(ys)])
        elif p < 0.75:
            ops.append(["trim", rnd.choice(["start", "end"])])
        elif p < 0.8:
            ops.append(["trimtext", rnd.choice(["start", "end"]), rnd.choice(["0:01", "3.5", "x", "0:12.25", "99"])])
        elif p < 0.85:
            ops.append(["speedtext", -1, rnd.choice(["3", "0.5", "bad", "1,25x", "0.05"])])
        elif p < 0.88:
            ops.append(["fit"])
        else:
            ops.append(["down", round(rnd.uniform(0, W), 1), rnd.choice(ys)])
            held = True
    # speedtext -1 = the last section (skipped when there is none)
    fixed = []
    r = FakeRow(W, {"w": 1920, "h": 1080, "dur": D, "fps": 60.0})
    steps = []
    for op in ops:
        if op[0] == "speedtext":
            if not r.sections:
                continue
            op = ["speedtext", len(r.sections) - 1, op[2]]
        apply(r, op)
        steps.append({"op": op, "state": state(r)})
    return {"name": f"random walk {seed}", "W": W, "dur": D, "w": 1920, "h": 1080, "steps": steps}


def thumbs():
    """Thumbnail layout: width, n1, level and the needed indexes for several views."""
    out = []
    for (w, h, dur, W) in ((1920, 1080, 20.0, 1000), (3440, 1440, 12.0, 1600), (1080, 1920, 300.0, 700),
                           (640, 480, 2.5, 2400)):
        r = FakeRow(W, {"w": w, "h": h, "dur": dur, "fps": 30.0})
        tw = max(48, min(240, int(m.THUMB_H * w / h)))
        n1 = max(5, -(-(max(W, 800) - 2 * m.PAD) // tw))
        for va, vl in ((0.0, dur), (dur * 0.3, dur / 7), (dur * 0.9, dur / 40), (dur * 0.5, dur / 3)):
            r.view_a, r.view_len = va, vl
            L = r.level()
            count = n1 * (2 ** L)
            step = dur / count
            lo = int(max(0, math.floor((r.view_a - 0.25 * r.view_len) / step)))     # reference refresh_thumbs
            hi = int(min(count - 1, math.ceil((r.view_a + 1.25 * r.view_len) / step)))
            mid = r.view_a + r.view_len / 2
            need = sorted(range(lo, hi + 1), key=lambda k: abs((k + 0.5) * step - mid))
            out.append({"w": w, "h": h, "dur": dur, "W": W, "view_a": va, "view_len": vl, "tw": tw, "n1": n1,
                        "level": L, "need": need})
    return out


def main():
    data = {"constants": {"PAD": m.PAD, "TL_H": m.TL_H, "LANE_H": m.LANE_H, "ZLANE_H": m.ZLANE_H,
                          "COMPACT_TL_H": m.COMPACT_TL_H, "HANDLE_GRAB": m.HANDLE_GRAB, "THUMB_H": m.THUMB_H},
            "cases": scripted() + [random_walk(s) for s in (1, 2, 3, 4, 5, 6)],
            "thumbs": thumbs()}
    with open(os.path.join(dfx.OUT, "timeline.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1, allow_nan=False, ensure_ascii=False)
        f.write("\n")
    print("timeline.json:", len(data["cases"]), "cases,", sum(len(c["steps"]) for c in data["cases"]), "steps,",
          len(data["thumbs"]), "thumbnail layouts")


if __name__ == "__main__":
    main()
