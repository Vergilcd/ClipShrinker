import sys, time, importlib.util, os, json, shutil
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.winsound=None
SESS=S+"/t/_sess.json"; vs.SESSION_FILE=SESS
st={"geometry":"1280x800+0+0","restore_session":False}
vs.save_settings=lambda d: st.update(d); vs.load_settings=lambda: dict(st)
from unittest.mock import MagicMock
vs.messagebox=MagicMock(); vs.messagebox.askyesno.return_value=True
app=vs.App()
def pump(n=10):
    for i in range(n): app.update(); time.sleep(0.03)
pump(); app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/LONG_5min.mp4"]); pump(20)
r=app.rows
r[0].start,r[0].end=0.5,3.0; r[0].sections.append(r[0].new_section(1.0,2.0,3.0))
r[2].start,r[2].end=30.0,90.0
app.size_var.set("25"); app.crop_var.set("16:9"); app.mode_var.set("join"); app.mutemic_var.set(True)
app.set_active(r[2]); app.project_path=S+"/t/_p.clipproj"
vs.filedialog=MagicMock(); vs.filedialog.asksaveasfilename.return_value=S+"/t/_p.clipproj"
app.save_project(True); print("saved:", os.path.exists(S+"/t/_p.clipproj"), "title:", app.title()[:40])
d=json.load(open(S+"/t/_p.clipproj")); print("clips", [(os.path.basename(c["path"]),c["start"],c["end"],len(c["sections"]),c.get("rel")) for c in d["clips"]], d["active"], d["settings"]["size"], d["settings"]["mode"])
app.clear_all(); app.size_var.set("99"); app.crop_var.set("Original (no crop)"); app.mode_var.set("separate"); app.mutemic_var.set(False); pump(5)
app.open_project(S+"/t/_p.clipproj"); pump(20)
r=app.rows
print("reloaded", [(os.path.basename(x.path),x.start,x.end,[(s["a"],s["b"],s["speed"],s["var"].get()) for s in x.sections]) for x in r])
print("settings", app.size_var.get(), app.crop_var.get(), app.mode_var.get(), app.mutemic_var.get(), "active", app.rows.index(app.active_row), "summary", app.summary_lbl.cget("text"))
# missing file + relative fallback
d["clips"][1]["path"]="/nonexistent/D_2aud.mp4"; d["clips"][1]["rel"]="D_2aud.mp4"; d["clips"][0]["path"]="/nonexistent/x.mp4"; d["clips"][0]["rel"]="nope.mp4"
json.dump(d,open(S+"/t/_p2.clipproj","w"))
app.load_project(S+"/t/_p2.clipproj"); pump(10); print("with missing:", [os.path.basename(x.path) for x in app.rows], "warn called:", vs.messagebox.showwarning.called)
# bad file
open(S+"/t/_bad.clipproj","w").write("nope"); print("bad:", app.load_project(S+"/t/_bad.clipproj"), "rows kept", len(app.rows))
# session restore
app.restore_var.set(True); app._save_session(); print("session file:", os.path.exists(SESS))
app.destroy()
st["restore_session"]=True
app2=vs.App(); 
for i in range(40): app2.update(); time.sleep(0.03)
print("restored rows (setting on):", len(app2.rows))
app2.restore_var.set(False); app2._save_session(); print("session removed when off:", not os.path.exists(SESS)); app2.destroy()
st["restore_session"]=False
app3=vs.App(); 
for i in range(40): app3.update(); time.sleep(0.03)
print("rows with setting off:", len(app3.rows)); app3.destroy()
