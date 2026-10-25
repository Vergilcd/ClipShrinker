import sys, os, types, time, importlib.util, subprocess
from unittest.mock import MagicMock
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
class W:
    def __init__(self,*a,**k): self.__dict__["_m"]={}
    def __getattr__(self,n):
        if n.startswith("__"): raise AttributeError(n)
        if n=="winfo_width": return lambda: 1000
        if n=="winfo_height": return lambda: 600
        if n=="winfo_ismapped": return lambda: True
        if n=="winfo_screenwidth": return lambda: 1920
        if n=="winfo_screenheight": return lambda: 1080
        return self.__dict__["_m"].setdefault(n, MagicMock(name=n))
    def __setitem__(self,k,v): pass
class Var:
    def __init__(self,value="",**k): self.v=value
    def get(self): return self.v
    def set(self,v): self.v=v
    def trace_add(self,*a): pass
IMGS=[]
class Img:
    def __init__(self,**k): IMGS.append(k.get("data")); 
tk=types.ModuleType("tkinter"); tk.Tk=W; tk.Frame=W; tk.Canvas=W; tk.Toplevel=W; tk.Label=W; tk.StringVar=Var; tk.BooleanVar=Var; tk.IntVar=Var; tk.PhotoImage=Img
ttk=types.ModuleType("tkinter.ttk")
for n in ("Frame","Label","Button","Entry","Combobox","Radiobutton","Separator","Spinbox","Scrollbar","Progressbar","LabelFrame","PanedWindow","Checkbutton","Scale"): setattr(ttk,n,W)
fd=types.ModuleType("tkinter.filedialog"); mb=types.ModuleType("tkinter.messagebox")
for n in ("showinfo","showwarning","showerror"): setattr(mb,n,MagicMock())
tk.ttk=ttk; tk.filedialog=fd; tk.messagebox=mb
sys.modules.update({"tkinter":tk,"tkinter.ttk":ttk,"tkinter.filedialog":fd,"tkinter.messagebox":mb})
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"



vs.winsound=None
app=vs.App()
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
files=[S+"/t/"+n for n in ("A_wide.mp4","B_16x9.mp4","D_2aud.mp4","LONG_5min.mp4","K4_2aud.mp4")]*3
order=[]
orig=vs.grab_frames
def spy(path,times,*a,**k):
    order.append(os.path.basename(path)); return orig(path,times,*a,**k)
vs.grab_frames=spy
vs.ClipRow.after=lambda self,ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
t=time.time(); app.add_files(files)
# make the 13th clip active right away
time.sleep(0.2)
deadline=time.time()+120
while time.time()<deadline:
    if all(len(r.cache)>=r.n1 for r in app.rows if r.n1): break
    time.sleep(0.2)
print("15 clips, all thumbnails after %.1f s"%(time.time()-t), [len(r.cache) for r in app.rows])
print("first jobs:", order[:6])
app.on_close()
