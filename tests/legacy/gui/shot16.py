import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
th=sys.argv[1]
if len(sys.argv)>2: vs.numpy_ready=lambda: True
vs.load_settings=lambda: {"theme":th,"geometry":"1180x800+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.04)
pump(10)
app.add_files([S+"/t/pan.mp4"]); pump(25)
row=app.rows[0]
def pv():
    d=vs.grab_frame(row.path,row.playhead,1164,360,None,None); app._show_preview(d); pump(8)
pv(); row.playhead=1.0
app._on_z(None); pump(15); pv(); pump(10)
print("cb state",app.track_cb.cget("state"),app.track_var.get())
if len(sys.argv)>2:
    app.track_cb.invoke(); pump(5)
else:
    vs.messagebox.askyesno=lambda *a,**k: False
    app.track_cb.invoke(); pump(5)
print("track",row.zsel.get("track"),app.track_var.get(), app.status.cget("text"))
print("clip zooms",row.clip()["zooms"])
app.undo() if hasattr(app,"undo") else None; pump(3); print("after undo",row.zooms[0].get("track") if row.zooms else None)
if len(sys.argv)>2:
    row.zooms[0]["track"]=True; row.zsel=row.zooms[0]; row.redraw(); app.bracket_refresh()
pump(10)
os.system(f"import -window root {S}/gui/v103_{th}_a.png")
app.show_settings(); pump(15)
os.system(f"import -window root {S}/gui/v103_{th}_s.png")
app.destroy()
