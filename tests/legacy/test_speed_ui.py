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
tk=types.ModuleType("tkinter"); tk.Tk=W; tk.Frame=W; tk.Canvas=W; tk.Label=W; tk.Toplevel=W; tk.Menu=W; tk.Entry=W; tk.StringVar=Var; tk.BooleanVar=Var; tk.IntVar=Var; tk.PhotoImage=lambda **k: object()
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
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
app.add_files([f"{S}/t/A_wide.mp4",f"{S}/t/B_16x9.mp4"])
r=app.rows[0]; r.canvas.winfo_width=lambda:1000
print("dur",r.info["dur"])
class E:
    state=0
                        # fake mouse event
    def __init__(s,x,y=60): s.x=x; s.y=y
def X(t): return r._x(t)

# S hotkey at playhead
r.playhead=1.0; app.set_active(r)
print("typing?",app._typing()); app._on_s(None)
print("sections after S:",[(round(s["a"],2),round(s["b"],2),s["speed"]) for s in r.sections])
r.playhead=1.5; r.add_section()   # inside the first -> placed after it
print("second:",[(round(s["a"],2),round(s["b"],2)) for s in r.sections])
r.playhead=0.0; r.add_section()
print("third:",[(round(s["a"],2),round(s["b"],2)) for s in sorted(r.sections,key=lambda s:s["a"])])
# drag right handle of 1st section into the neighbour (must be limited)
s1=sorted(r.sections,key=lambda s:s["a"])[1]
r.on_down(E(X(s1["b"]))); print("drag mode",r.drag[0]); r.on_move(E(X(3.9))); r.on_move(E(X(3.9)))
nxt=sorted(r.sections,key=lambda s:s["a"])[2]
print("limited by neighbour:", round(s1["b"],3) <= round(nxt["a"],3)+1e-6, round(s1["b"],2), round(nxt["a"],2))
# move whole section via lane
r.drag=None; sA=sorted(r.sections,key=lambda s:s["a"])[0]
mid=(sA["a"]+sA["b"])/2
r.on_down(E(X(mid),y=10)); print("lane drag mode:",r.drag[0]); r.on_move(E(X(mid)+30)); r.drag=None
print("moved first:",round(sA["a"],2),round(sA["b"],2))
# text input
sA["var"].set("3,5"); r._entry_commit(sA,True); print("speed ->",sA["speed"],sA["var"].get())
sA["var"].set("abc"); r._entry_commit(sA,True); print("invalid keeps ->",sA["speed"])
sA["var"].set("0"); r._entry_commit(sA,True); print("zero keeps ->",sA["speed"])
sA["var"].set("500"); r._entry_commit(sA,True); print("clamp ->",sA["speed"])
sA["var"].set("4x"); r._entry_commit(sA,True)
# trim over sections: visible clipping
r.start=1.2; print("vis after trim start 1.2:",[(round(a,2),round(b,2)) for _,a,b in r.vis()])
r.start=0.0
c=r.clip(); print("pieces:",[(round(a,2),round(b,2),sp) for a,b,sp in c["pieces"]], "dur", round(vs.pieces_duration(c["pieces"]),2))
# right click remove third
last=sorted(r.sections,key=lambda s:s["a"])[2]
r.on_right(E(X((last["a"]+last["b"])/2))); print("after right-click remove:",len(r.sections))
r.sel=r.sections[0]; r.remove_section(r.sel); print("after delete:",len(r.sections))
# export with sections
app.rows[0].end=4.0
app.size_var.set("0.7"); app.mode_var.set("separate")
fd.askdirectory.return_value=S+"/t/outdir2"
c=r.clip(); print("export pieces:",len(c["pieces"]) if c["pieces"] else None)
import time
app.start_export()
t0=time.time()
while app.encoding and time.time()-t0<120: time.sleep(0.2)
time.sleep(0.3)
print("dialog:", (mb.showinfo.call_args[0][1] if mb.showinfo.called else mb.showwarning.call_args))
# playback with sections: collect playhead times
r.sections.clear(); r.sel=None; r.start,r.end=0.0,4.0
r.playhead=0.0; r.add_section(); r.sections[0]["a"],r.sections[0]["b"],r.sections[0]["speed"]=1.0,3.0,4.0
seen=[]
app.sound_var.set(False) if hasattr(app,"sound_var") else None
orig=r.set_playhead_fast
r.set_playhead_fast=lambda t:(seen.append(round(t,2)),orig(t))
app._draw_frame_orig=app._draw_frame
app.preview.config=lambda **k:None
app.start_playback(); time.sleep(3.5); app.stop_playback()
print("playhead source times (every 10th):",seen[::10][:14])
print("monotone first pass:",all(b>=a for a,b in zip(seen,seen[1:]) if b>a-0.01) )
