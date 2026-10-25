import sys, os, types, time, importlib.util
from unittest.mock import MagicMock
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"

class W:                                   # fake widget
    def __init__(self,*a,**k): self.__dict__["_m"]={}
    def __getattr__(self,n):
        if n.startswith("__"): raise AttributeError(n)
        if n=="winfo_width": return lambda: 1000
        if n=="winfo_height": return lambda: 600
        if n=="winfo_screenwidth": return lambda: 1920
        if n=="winfo_screenheight": return lambda: 1080
        return self.__dict__["_m"].setdefault(n, MagicMock(name=n))
    def __setitem__(self,k,v): pass
class Var:
    def __init__(self,value="",**k): self.v=value
    def get(self): return self.v
    def set(self,v): self.v=v
    def trace_add(self,*a): pass
tk=types.ModuleType("tkinter"); tk.Tk=W; tk.Frame=W; tk.Canvas=W; tk.Label=W; tk.Toplevel=W; tk.Menu=W; tk.StringVar=Var; tk.BooleanVar=Var; tk.IntVar=Var; tk.PhotoImage=lambda **k: object()
ttk=types.ModuleType("tkinter.ttk")
for n in ("Frame","Label","Button","Entry","Combobox","Radiobutton","Separator","Spinbox","Scrollbar","Progressbar","LabelFrame","PanedWindow","Checkbutton","Scale"): setattr(ttk,n,W)
fd=types.ModuleType("tkinter.filedialog"); mb=types.ModuleType("tkinter.messagebox")
fd.askopenfilenames=MagicMock(); fd.asksaveasfilename=MagicMock(); fd.askdirectory=MagicMock()
for n in ("showinfo","showwarning","showerror"): setattr(mb,n,MagicMock())
tk.ttk=ttk; tk.filedialog=fd; tk.messagebox=mb
sys.modules.update({"tkinter":tk,"tkinter.ttk":ttk,"tkinter.filedialog":fd,"tkinter.messagebox":mb})
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
vs.video_codec_args=lambda codec,v_bps:["-c:v","mpeg4","-b:v",f"{max(20,int(v_bps/1000))}k","-maxrate",f"{max(20,int(v_bps/1000))}k","-bufsize",f"{max(20,int(v_bps/1000))*2}k","-pix_fmt","yuv420p"]


import json, subprocess, shutil
vs.PRESETS_FILE=S+"/t/presets_test.json"
if os.path.exists(vs.PRESETS_FILE): os.remove(vs.PRESETS_FILE)
app=vs.App(); app.ask_export_mode=lambda n: app.mode_var.get()=="join"
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
app.add_files([f"{S}/t/A_wide.mp4",f"{S}/t/B_16x9.mp4",f"{S}/t/C_noaudio.mp4"])
print("rows",len(app.rows),"presets",app.presets,"labels",app.preset_labels())
app.presets=[{"name":"Discord","mbps":1.5,"key":"q"},{"name":"Archive","mbps":4.0,"key":"w"}]
app._presets_changed()
print("saved file:",json.load(open(vs.PRESETS_FILE)))
print("loaded back:",vs.load_presets()==app.presets, "labels",app.preset_labels())
# hotkey on active + selection
class Ev:
    def __init__(s,k,state=0): s.keysym=k; s.state=state
app.active_row=app.rows[0]; app.selection={app.rows[0]}
app._typing=lambda: False
r=app._on_any_key(Ev("q")); print("q ->",r,[x.preset for x in app.rows])
r=app._on_any_key(Ev("q",4)); print("ctrl+q ignored ->",r)
r=app._on_any_key(Ev("x")); print("unknown key ->",r)
app.selection={app.rows[1],app.rows[2]}; app._on_any_key(Ev("w")); print("w on selection ->",[x.preset for x in app.rows])
# undo / redo
app.undo(); print("undo ->",[x.preset for x in app.rows]); app.redo(); print("redo ->",[x.preset for x in app.rows])
# project round trip
d=app.project_data(); print("project presets:",[c["preset"] for c in d["clips"]])
# delete a preset: clips fall back to Auto
app.presets=[p for p in app.presets if p["name"]!="Archive"]; app._presets_changed(); print("after delete Archive ->",[x.preset for x in app.rows])
# export separate with per-clip bitrate
for r in app.rows: r.end=min(r.end,2.0)
app.rows[0].set_preset("Discord")
app.size_var.set("0.8"); app.mode_var.set("separate")
out=S+"/t/outdir_p"; shutil.rmtree(out,ignore_errors=True); os.makedirs(out)
fd.askdirectory.return_value=out
app.start_export()
t0=time.time()
while app.encoding and time.time()-t0<200: time.sleep(0.2)
time.sleep(0.3)
for f in sorted(os.listdir(out)):
    pr=subprocess.run([vs.FFPROBE,"-v","error","-show_entries","format=bit_rate,size","-of","csv=p=0",os.path.join(out,f)],capture_output=True,text=True).stdout.strip()
    print(f, "bit_rate,size =",pr)
print("status:",app.status.config.call_args)
# join ignores presets
app.mode_var.set("join"); fd.asksaveasfilename.return_value=S+"/t/outdir_p/joined.mp4"
app.start_export()
t0=time.time()
while app.encoding and time.time()-t0<200: time.sleep(0.2)
time.sleep(0.3); print("joined exists:",os.path.exists(S+"/t/outdir_p/joined.mp4"))
