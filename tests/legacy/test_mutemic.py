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
    def set(self,v):
        self.v=v
        for cb in getattr(self,'cbs',[]): cb()
    def trace_add(self,mode,cb): self.cbs=getattr(self,'cbs',[])+[cb]
tk=types.ModuleType("tkinter"); tk.Tk=W; tk.Frame=W; tk.Canvas=W; tk.Label=W; tk.StringVar=Var; tk.BooleanVar=Var; tk.IntVar=Var; tk.PhotoImage=lambda **k: object()
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



import time, subprocess, json
app=vs.App()
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
app.add_files([f"{S}/t/D_2aud.mp4"])
def n_audio(p):
    r=subprocess.run([vs.FFPROBE,"-v","error","-print_format","json","-show_streams",p],capture_output=True,text=True)
    return sum(1 for s in json.loads(r.stdout)["streams"] if s["codec_type"]=="audio")
def export(out,mix):
    fd.asksaveasfilename.return_value=out; app.atrk_var.set("Mix into one track" if mix else "Keep separate tracks")
    app.size_var.set("0.8"); app.bitrate_var.set(""); mb.showinfo.reset_mock(); mb.showerror.reset_mock()
    app.start_export(); t0=time.time()
    while app.encoding and time.time()-t0<100: time.sleep(0.2)
    time.sleep(0.3); return n_audio(out)
print("unmuted separate:",export(S+"/t/outdir/m1.mp4",False),"tracks")
app.mutemic_var.set(True)
print("muted separate  :",export(S+"/t/outdir/m2.mp4",False),"track(s)")
print("muted mix       :",export(S+"/t/outdir/m3.mp4",True),"track(s)")
# preview wav track count used
cap={}
orig=vs.make_preview_wav
vs.make_preview_wav=lambda *a,**k:(cap.setdefault("args",a),orig(*a,**k))[1]
app.sound_var.set(True); vs.winsound=object()
app.preview.config=lambda **k:None
app.start_playback(); time.sleep(2); app.stop_playback()
print("preview naudio arg (muted):",cap.get("args",[None]*7)[6] if cap.get("args") else "n/a")
cap.clear(); app.mutemic_var.set(False)
app.start_playback(); time.sleep(2); app.stop_playback()
print("preview naudio arg (unmuted):",cap["args"][6] if cap.get("args") else "n/a")
