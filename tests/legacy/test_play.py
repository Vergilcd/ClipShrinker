import sys, os, types, time, importlib.util, subprocess
from unittest.mock import MagicMock
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
class W:
    def __init__(self,*a,**k): self.__dict__["_m"]={}
    def __getattr__(self,n):
        if n.startswith("__"): raise AttributeError(n)
        if n=="winfo_width": return lambda: 1000
        if n=="winfo_height": return lambda: 600
        return self.__dict__["_m"].setdefault(n, MagicMock(name=n))
    def __setitem__(self,k,v): pass
class Var:
    def __init__(self,value="",**k): self.v=value
    def get(self): return self.v
    def set(self,v): self.v=v
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

app=vs.App()
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
app.add_files([S+"/t/A_wide.mp4"])
row=app.rows[0]; row.start, row.end = 0.5, 1.5          # 1 s section, looping
app.crop_var.set("21:9")
firsts=[]; frames=[0]
orig=app._deliver
def deliver(gen,buf,t,first,wav):
    frames[0]+=1
    if first: firsts.append(time.time())
    orig(gen,buf,t,first,wav)
app._deliver=deliver
pass
t0=time.time(); app.start_playback(); time.sleep(3.3); 
print(f"playing={app.playing} dims={app._play_dims} hw_after_fallback={app._play_hw}")
print(f"frames in 3.3 s: {frames[0]} (30 fps loop of 1 s: ~99 minus restart gaps) | loop starts: {len(firsts)}")
print("loop restart gaps (s):", [round(b-a,2) for a,b in zip(firsts,firsts[1:])])
w,h=app._play_dims
hdr=b"P6 %d %d 255\n"%(w,h)
ok=[d for d in IMGS if isinstance(d,bytes) and d.startswith(hdr) and len(d)==len(hdr)+w*h*3]
print(f"PhotoImage draws: {len(IMGS)}, valid PPM payloads: {len(ok)}")
from PIL import Image
Image.frombytes("RGB",(w,h),ok[len(ok)//2][len(hdr):]).save(S+"/t/play_frame.png")
print("playhead moved to:", row.set_playhead_fast.call_args if hasattr(row.set_playhead_fast,'call_args') else None)
t1=time.time(); app.toggle_play(); print(f"pause -> playing={app.playing} (took {time.time()-t1:.2f}s)")
time.sleep(0.5)
n=frames[0]; time.sleep(0.7); print("frames after pause:", frames[0]-n)
print("ffmpeg still running:", subprocess.run("pgrep -f 'linux/ffmpeg.*rawvideo' | wc -l",shell=True,capture_output=True,text=True).stdout.strip())
# audio prep
wav=vs.make_preview_wav(S+"/t/A_wide.mp4",0.5,1.0,"t"); print("wav:",wav, os.path.getsize(wav)//1024,"KB"); vs.cleanup_wavs(); print("wav cleaned:", not os.path.exists(wav))
# space handler path + crop change restart
app.start_playback(); time.sleep(0.6); g=app.play_gen; app.crop_var.set("16:9"); app._crop_changed(); time.sleep(0.6)
print("crop change restarted playback:", app.play_gen>g, "dims", app._play_dims, "playing", app.playing)
app.on_close(); print("closed, playing:", app.playing)
