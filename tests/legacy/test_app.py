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

app=vs.App(); app.ask_export_mode=lambda n: app.mode_var.get()=="join"; app.presets=[]
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)   # run zero-delay callbacks inline
files=[f"{S}/t/A_wide.mp4",f"{S}/t/B_16x9.mp4",f"{S}/t/C_noaudio.mp4",f"{S}/t/nonexistent.mp4",f"{S}/t/test_app.py"]
app.add_files(files)
print("rows:",[os.path.basename(r.path) for r in app.rows], "| warning shown:", mb.showwarning.call_args[0][1].replace("\n"," / "))
app.move_row(app.rows[2],-1); print("after move:",[os.path.basename(r.path) for r in app.rows])
app.crop_var.set("21:9"); app._update_crop_info(); print("crop info ->", app.crop_info.config.call_args)
# trim by simulating handle drags
app.rows[0].start, app.rows[0].end = 0.5, 3.0
app.rows[1].end = 2.0; app.rows[2].end = 2.0
app.size_var.set("0.8")
def wait():
    t0=time.time()
    while app.encoding and time.time()-t0<200: time.sleep(0.2)
    time.sleep(0.2)

# 1) separate -> directory
app.mode_var.set("separate"); fd.askdirectory.return_value=S+"/t/outdir"
app.start_export(); wait()
print("separate:", sorted(os.listdir(S+"/t/outdir")), "| dialog:", mb.showinfo.call_args[0][1].replace("\n"," / ") if mb.showinfo.called else mb.showwarning.call_args)
# 2) join with gradient wipe
app.mode_var.set("join"); app.trans_var.set("Gradient wipe, left to right"); app.tlen_var.set("0,6")
fd.asksaveasfilename.return_value=S+"/t/outdir/joined.mp4"; app.size_var.set("1.2")
app.start_export(); wait()
print("join:", os.path.exists(S+"/t/outdir/joined.mp4"), os.path.getsize(S+"/t/outdir/joined.mp4")/1048576, "MB; leftover parts:", [f for f in os.listdir(S+"/t/outdir") if "part" in f])
# 3) cancel mid-way
app.size_var.set("1.5"); fd.asksaveasfilename.return_value=S+"/t/outdir/cancelled.mp4"
app.start_export(); time.sleep(0.4); app.cancel(); wait()
print("cancel: leftover files:", sorted(f for f in os.listdir(S+"/t/outdir") if "cancel" in f or "part" in f))
app.remove_row(app.rows[0]); app.clear_all(); print("cleared rows:", len(app.rows))
