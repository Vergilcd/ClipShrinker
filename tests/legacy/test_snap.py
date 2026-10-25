import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
vs.load_settings=lambda: {"theme":"dark","geometry":"1180x720+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.03)
pump(10); app.add_files([S+"/t/LONG_5min.mp4"]); pump(25)
row=app.rows[0]; dur=row.info["dur"]
class E:
    def __init__(s,x,y=60): s.x=x; s.y=y; s.state=0
row.start,row.end=20.0,200.0; row.redraw(); pump(3)
def drag(x0,x1,y=60):
    row.on_down(E(x0,y)); row.on_move(E(x1,y)); row.drag=None
# park playhead at 100 s
row.playhead=100.0
x100=row._x(100.0)
# drag end handle (at 200s) to 5px right of the playhead -> snaps
row.on_down(E(row._x(200.0))); row.on_move(E(x100+5)); print("end snapped:",row.end); row.drag=None
row.end=200.0; row.playhead=100.0
row.on_down(E(row._x(200.0))); row.on_move(E(x100+40)); print("end far (no snap):",round(row.end,2)); row.drag=None
# start handle
row.start=20.0; row.playhead=100.0
row.on_down(E(row._x(20.0))); row.on_move(E(x100-6)); print("start snapped:",row.start); row.drag=None
# section edge
row.start,row.end=20.0,250.0
sec=row.new_section(140.0,170.0,2.0); row.sections.append(sec); row.redraw(); pump(3)
row.playhead=100.0
row.on_down(E(row._x(140.0))); row.on_move(E(x100+4)); print("sa snapped:",sec["a"]); row.drag=None
sec["a"],sec["b"]=140.0,170.0; row.playhead=100.0
row.on_down(E(row._x(170.0))); row.on_move(E(row._x(190.0))); print("sb free:",round(sec["b"],1)); row.drag=None
sec["a"],sec["b"]=140.0,170.0; row.playhead=160.0; row.redraw()
# move the section so that its end gets near the playhead(160)->  grab bar at 150 (lane y small), move so b ~ 163
row.on_down(E(row._x(150.0),10)); print("drag:",row.drag if not isinstance(row.drag,tuple) else row.drag[0], "snap_t",row.snap_t)
row.on_move(E(row._x(150.0)-row._x(170.0)+row._x(162.0)+0,10)); print("move a,b:",round(sec["a"],2),round(sec["b"],2)); row.drag=None
app.destroy()
