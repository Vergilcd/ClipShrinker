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


import time
app=vs.App(); app.ask_export_mode=lambda n: app.mode_var.get()=="join"; app.presets=[]
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
print("default codec:",app.codec_var.get())
app.add_files([f"{S}/t/A_wide.mp4"])
app.rows[0].end=2.5
def last(m): return m.config.call_args
# bitrate typed
app.res_var.set("75%")
app.bitrate_var.set("2,5")
print("bitrate parsed:",app._bitrate(),"| size entry:",last(app.size_entry)," | hint:",last(app.size_hint))
print("res values:",last(app.res_cb)," res now:",app.res_var.get())
app.res_var.set("Original")
# invalid text -> treated as empty by the field logic, export complains
app.bitrate_var.set("abc"); print("invalid parsed:",app._bitrate(),"| size entry:",last(app.size_entry),"| res:",app.res_var.get())
app.start_export(); print("error dialog:",mb.showerror.call_args[0])
# clear -> back to size mode
app.bitrate_var.set(""); print("cleared: size entry",last(app.size_entry),"res values",last(app.res_cb),"res",app.res_var.get())
# mode change keeps hint right
app.bitrate_var.set("3"); app.mode_var.set("join"); app._mode_changed(); print("hint in join+bitrate:",last(app.size_hint))
app.mode_var.set("separate"); app._mode_changed()
# export with bitrate while size field holds garbage
app.size_var.set("garbage")
fd.asksaveasfilename.return_value=S+"/t/outdir/br_ui.mp4"
app.bitrate_var.set("1.2")
mb.showinfo.reset_mock()
app.start_export()
t0=time.time()
while app.encoding and time.time()-t0<100: time.sleep(0.2)
time.sleep(0.3)
print("dialog:",mb.showinfo.call_args[0][1].replace("\n"," / ") if mb.showinfo.called else mb.showwarning.call_args)
print("file:",os.path.exists(S+"/t/outdir/br_ui.mp4"))
