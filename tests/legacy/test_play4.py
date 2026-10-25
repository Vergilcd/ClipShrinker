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
EVT=[]
class FakeMM:
    def open(self,rate,ch,bits,nbuf,blk):
        self.done=[time.time()]*nbuf; self.t=0; self.bytes=0; EVT.append(("open",time.time())); self.paused=True; self.closed=False
    def pause(self): EVT.append(("pause",time.time()))
    def resume(self): EVT.append(("resume",time.time())); self.paused=False; self.t0=time.time()
    def free(self,i): return self.done[i] is not None and (time.time()>=self.done[i])
    def all_free(self): return all(self.free(i) for i in range(len(self.done)))
    def write(self,i,data):
        self.bytes+=len(data); self.done[i]=time.time()+ (999 if self.paused else 0.1*10)  # crude: busy for 1 s
        if not self.paused: pass
    def reset(self): EVT.append(("reset",time.time()))
    def close(self): EVT.append(("close",time.time())); self.closed=True
FM=FakeMM()
vs._WinMM=lambda: FM
app=vs.App()
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
app._wave_ok=True
app.add_files([S+"/t/K4_2aud.mp4"])
row=app.rows[0]; row.start,row.end=5.0,25.0; row.playhead=15.0
app.sound_var.set(True)
t0=time.time(); app.start_playback(); time.sleep(3.0)
print("events:", [(e,round(t-t0,2)) for e,t in EVT])
print("bytes streamed to player so far:", FM.bytes, "(paused buffers block feeder at", 10*17640,"until resume)")
print("winsound used:", len(WS.calls))
app.stop_playback(); time.sleep(0.6)
print("events after stop:", [(e,round(t-t0,2)) for e,t in EVT][-3:])
import subprocess
print("ffmpeg left:", subprocess.run("pgrep -f 'linux/ffmpeg' | wc -l",shell=True,capture_output=True,text=True).stdout.strip())
app.on_close()
