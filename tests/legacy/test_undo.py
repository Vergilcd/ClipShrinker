import sys, time, importlib.util, os
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.winsound=None
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0"}
from unittest.mock import MagicMock
vs.messagebox=MagicMock(); vs.messagebox.askyesno.return_value=True
app=vs.App()
def pump(n=10):
    for i in range(n): app.update(); time.sleep(0.03)
class E:
    def __init__(s,state=0,x=300,y=20): s.state,s.x,s.y=state,x,y
names=lambda: [os.path.basename(r.path)[:1]+str(i) for i,r in enumerate(app.rows)]
pump(); app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/B_16x9.mp4",S+"/t/LONG_5min.mp4"]); pump(25)
print("loaded", names(), "undo steps", len(app.undo_stack), "(1 expected: add clips)")
r0=app.rows[0]; ids=[id(r) for r in app.rows]
# delete 2 clips
app._on_delete(None) if False else None
app.rows[1]._hdr_down(E()); app.rows[2]._hdr_down(E(0x4)); app.delete_selected(); pump(5)
print("after delete", names())
app.undo(); pump(10); print("undo ->", names(), "same objects:", [id(r) for r in app.rows]==ids, "visible:", [r.winfo_ismapped() for r in app.rows])
app.redo(); pump(5); print("redo ->", names())
app.undo(); pump(5)
# trim via hotkey, section, speed
r=app.rows[0]; r.playhead=1.0; app.set_active(r); r.set_trim("start"); r.playhead=3.0; r.set_trim("end"); print("trim", r.start, r.end)
r.playhead=1.5; r.add_section(); print("sections", len(r.sections))
sec=r.sections[0]; sec["var"].set("5x"); r._entry_commit(sec, False); print("speed", sec["speed"])
app.undo(); print("undo speed ->", r.sections[0]["speed"], "(2 expected)")
app.undo(); print("undo section ->", len(r.sections))
app.undo(); print("undo trim end ->", r.end); app.undo(); print("undo trim start ->", r.start)
app.redo(); app.redo(); app.redo(); app.redo(); print("redo x4 ->", r.start, r.end, len(r.sections), r.sections[0]["speed"])
# drag trim handle
r.drag=None; r.on_down(E(0,x=r._x(r.end),y=60)); r.on_move(E(0,x=r._x(r.end)-60,y=60)); r.drag=None
e1=r.end; app.undo(); print("undo handle drag: end", e1, "->", r.end)
# reorder
app.redo()
app.rows[0]._hdr_down(E()); 
class M: y_root=0
last=app.rows[-1]; M.y_root=last.winfo_rooty()+last.winfo_height()-2; app.rows[0]._hdr_move(M); app.rows[0]._hdr_up(M); pump(3)
print("reordered", names()); app.undo(); pump(3); print("undo reorder", names())
# clear all and undo
app.clear_all(); print("cleared", len(app.rows)); app.undo(); pump(5); print("undo clear", names(), "empty label shown:", app.empty_lbl.winfo_ismapped())
# project load = one step
app.save_project  # skip dialog
import json; json.dump(app.project_data(), open(S+"/t/_u.clipproj","w"))
n=len(app.undo_stack); app.load_project(S+"/t/_u.clipproj"); pump(15); print("load project added steps:", len(app.undo_stack)-n, names())
app.undo(); pump(10); print("undo load ->", names())
print("detached kept:", len(app._detached), "stack", len(app.undo_stack), len(app.redo_stack))
app.destroy()
