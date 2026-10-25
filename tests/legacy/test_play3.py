import sys, os, types, time, importlib.util, subprocess
from unittest.mock import MagicMock
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
class W:
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
IMGS=[]
class Img:
    def __init__(self,**k): IMGS.append(k.get("data")); 
tk=types.ModuleType("tkinter"); tk.Tk=W; tk.Frame=W; tk.Canvas=W; tk.Label=W; tk.StringVar=Var; tk.BooleanVar=Var; tk.IntVar=Var; tk.PhotoImage=Img
ttk=types.ModuleType("tkinter.ttk")
for n in ("Frame","Label","Button","Entry","Combobox","Radiobutton","Separator","Spinbox","Scrollbar","Progressbar","LabelFrame","PanedWindow","Checkbutton","Scale"): setattr(ttk,n,W)
fd=types.ModuleType("tkinter.filedialog"); mb=types.ModuleType("tkinter.messagebox")
for n in ("showinfo","showwarning","showerror"): setattr(mb,n,MagicMock())
tk.ttk=ttk; tk.filedialog=fd; tk.messagebox=mb
sys.modules.update({"tkinter":tk,"tkinter.ttk":ttk,"tkinter.filedialog":fd,"tkinter.messagebox":mb})
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"


class WS:
    SND_FILENAME=1; SND_ASYNC=2; SND_PURGE=4
    calls=[]
    @staticmethod
    def PlaySound(a,f): WS.calls.append((time.time(),a,f))
vs.winsound=WS
app=vs.App()
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
app.add_files([S+"/t/K4_2aud.mp4"])
row=app.rows[0]; row.start,row.end=5.0,25.0; row.playhead=15.0
app.sound_var.set(True)
t0=time.time(); app.start_playback(); time.sleep(3.0)
print("lvl after fallback:", app._play_lvl, "playing", app.playing)
snd=[c for c in WS.calls if c[2]&1]
print("sound starts:", [(round(c[0]-t0,2), os.path.basename(c[1]) if c[1] else None) for c in snd])
time.sleep(2.5)
snd=[c for c in WS.calls if c[2]&1]
print("sound starts after 5.5s:", [(round(c[0]-t0,2), os.path.basename(c[1])) for c in snd])
if snd:
    import wave
    for c in snd: 
        w=wave.open(c[1]); print(os.path.basename(c[1]), round(w.getnframes()/w.getframerate(),2),"s")
app.stop_playback(); time.sleep(0.3); n=len(WS.calls); time.sleep(1); print("calls after stop:", len(WS.calls)-n)
print("cache entries:", len(app._wcache))
app.on_close()
