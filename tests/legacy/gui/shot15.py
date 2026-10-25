import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
th=sys.argv[1]
vs.load_settings=lambda: {"theme":th,"geometry":"1180x800+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.04)
pump(10)
app.add_files([S+"/t/B_16x9.mp4"]); pump(25)
row=app.rows[0]
def pv():
    d=vs.grab_frame(row.path,row.playhead,1164,360,None,None); app._show_preview(d); pump(8)
pv()
row.playhead=1.0
app.bind_all("<z>", app._on_z)
app._on_z(None); pump(15); pv()
row.add_section(2.0) if False else None
pump(30); print("img",repr(str(app.preview.cget("image"))),app.preview.winfo_width(),app.preview.winfo_height(),app.playing,app.active_row is row, row.zsel is not None); app.bracket_refresh(); print("vis",app._br_vis)
os.system(f"import -window root {S}/gui/v100_{th}_a.png")
# simulate drag of a corner of the bracket
class E: pass
z=row.zsel; px,py,pw,ph=app._br_target()[1]
e=E(); e.x_root=app.preview.winfo_rootx()+int((z["x"]+z["w"])*pw); e.y_root=app.preview.winfo_rooty()+int((z["y"]+z["w"])*ph)
app._br_down(e,"se"); e2=E(); e2.x_root=e.x_root+80; e2.y_root=e.y_root+60; app._br_move(e2); app._br_up()
print("after resize",z); pump(5)
e=E(); e.x_root=app.preview.winfo_rootx()+50; e.y_root=app.preview.winfo_rooty()+50
app._br_down(e,"move"); e2=E(); e2.x_root=e.x_root-40; e2.y_root=e.y_root+10; app._br_move(e2); app._br_up()
print("after move",z); pump(5)
os.system(f"import -window root {S}/gui/v100_{th}_b.png")
app.show_settings(); pump(15)
os.system(f"import -window root {S}/gui/v100_{th}_s.png")
app.destroy()
