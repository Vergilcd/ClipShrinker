import sys, time, importlib.util, os
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.winsound=None
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0"}
app=vs.App()
def pump(n=10):
    for i in range(n): app.update(); time.sleep(0.03)
pump(); app.add_files([S+"/t/A_wide.mp4"]*8); pump(25)
class E:
    def __init__(s,state=0,x=300,y=20): s.state,s.x,s.y=state,x,y
def idx(): return sorted(app.rows.index(r) for r in app.selection)
def hd(i,state=0): app.rows[i]._hdr_down(E(state))
print("active", app.rows.index(app.active_row), "sel", idx())
hd(3, 0x1); print("shift-click 3 ->", idx(), "active still", app.rows.index(app.active_row))
hd(5, 0x4); print("ctrl-click 5 ->", idx())
hd(1, 0x4); print("ctrl-click 1 ->", idx())
hd(1, 0x4); print("ctrl-click 1 again (toggle off) ->", idx())
print("selected look:", [r.selected for r in app.rows])
# shift from active (0) to 6 replaces selection
hd(6, 0x1); print("shift-click 6 ->", idx())
# timeline click with ctrl on collapsed strip
app.rows[7].on_down(E(0x4)); print("ctrl on timeline 7 ->", idx(), "active", app.rows.index(app.active_row), "tl_h7", app.rows[7].tl_h)
# plain click collapses
hd(2); print("plain click 2 ->", idx(), "active", app.rows.index(app.active_row))
# speed section delete has priority
r=app.rows[2]; r.playhead=1.0; r.add_section(); print("section added", len(r.sections), "sel", r.sel is not None)
hd(4,0x1); 
app._on_delete(None); print("delete with section selected: sections", len(r.sections), "rows", len(app.rows))
hd(2); hd(4,0x1); hd(7,0x4); print("sel", idx())
app._on_delete(None); print("after delete: rows", len(app.rows), "active", app.rows.index(app.active_row), "sel", idx(), [os.path.basename(x.path)[:1] for x in app.rows])
app._on_delete(None); print("delete active alone -> rows", len(app.rows))
app.select_all(); print("select all", idx()); app._on_delete(None); print("all deleted, rows", len(app.rows), "active", app.active_row)
app.destroy()
