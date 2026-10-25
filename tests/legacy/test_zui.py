import sys, os, time, importlib.util, json
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
vs.load_settings=lambda: {"theme":"dark","geometry":"1180x800+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.03)
pump(10); app.add_files([S+"/t/LONG_5min.mp4"]); pump(25)
row=app.rows[0]
class E:
    def __init__(s,x,y=60): s.x=x; s.y=y; s.state=0
ZY=vs.LANE_H+6
row.playhead=60.0
row.add_zoom(); z=row.zooms[0]; print("added",z["a"],z["b"],round(z["w"],2))
z["a"],z["b"]=60.0,100.0; row.redraw(); pump(3)
# resize end handle in the zoom lane
row.on_down(E(row._x(100.0),ZY)); row.on_move(E(row._x(130.0),ZY)); row.drag=None; print("b ->",round(z["b"],1))
# move the whole bar
row.on_down(E(row._x(80.0),ZY)); row.on_move(E(row._x(80.0)+50,ZY)); row.drag=None; print("moved",round(z["a"],1),round(z["b"],1))
# undo
app.undo(); pump(3); z2=row.zooms[0] if row.zooms else None; print("after undo",None if z2 is None else (round(z2["a"],1),round(z2["b"],1)))
app.redo(); pump(3); print("after redo",[(round(q["a"],1),round(q["b"],1)) for q in row.zooms])
# overlap with a speed section is allowed
row.playhead=70.0; row.add_section(2.0); print("speed+zoom overlap ok", len(row.sections), len(row.zooms))
# project round trip
d=app.project_data(); print(json.dumps(d["clips"][0]["zooms"]))
app.save_project_path=None
p=S+"/t/_z.clipproj"; json.dump(d,open(p,"w"))
app.load_project(p, quiet=True); pump(25); print('loaded zooms', [(round(q['a'],1),round(q['b'],1),q['w']) for q in app.rows[0].zooms], 'settings', app.zoom_fx_var.get(), app.zoom_len_var.get(), app.zoom_vol_var.get())
app.clear_all() if hasattr(app,"clear_all") and False else None
# remove with Delete
row.zsel=row.zooms[0]; app._on_delete(None); print("deleted zooms:",len(row.zooms))
app.undo(); pump(3); print("undo delete:",len(app.rows[0].zooms))
# clip dict
c=app.rows[0].clip(); print("clip zooms",c["zooms"])
app.destroy()
