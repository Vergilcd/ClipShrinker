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



import time
app=vs.App()
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
app.add_files([f"{S}/t/LONG_5min.mp4",f"{S}/t/D_2aud.mp4"])
r=app.rows[0]; r.canvas.winfo_width=lambda:1000
r.after=lambda ms,fn=None,*a:(fn(*a) if callable(fn) and ms==0 else None)
class E:
    def __init__(s,x,y=60,delta=0,state=0): s.x=x;s.y=y;s.delta=delta;s.state=state
print("title:",r.title_lbl.config.call_args[1]["text"] if r.title_lbl.config.call_args else None)
print("dur",r.info["dur"],"view",r.view_a,r.view_len,"zoom",r.zoom_factor(),"level",r.level())
# not zoomed: wheel falls through to list
print("wheel unzoomed ->",r.on_wheel(E(500,delta=120)))
# ctrl+wheel zoom in at x=500 (middle)
tm=r._t(500)
print("ctrl wheel ->",r.on_wheel(E(500,delta=120,state=4)))
print("zoom now %.2f, time under mouse before/after: %.2f -> %.2f"%(r.zoom_factor(),tm,r._t(500)))
for _ in range(8): r.on_wheel(E(500,delta=120,state=4))
print("zoom %.1f view %.2f..%.2f len %.2f level %d"%(r.zoom_factor(),r.view_a,r.view_a+r.view_len,r.view_len,r.level()))
# zoom limit
for _ in range(40): r.on_wheel(E(500,delta=120,state=4))
print("max zoom view_len:",round(r.view_len,3))
# plain wheel pans
a0=r.view_a; print("pan ->",r.on_wheel(E(500,delta=-120)), "view_a moved", round(r.view_a-a0,3))
r.on_wheel(E(500,delta=120)); print("pan back ->",round(r.view_a-a0,3))
# edges clamp
for _ in range(500): r.on_wheel(E(500,delta=-120))
print("clamped right: ",round(r.view_a+r.view_len,3),"== dur",r.info["dur"])
for _ in range(500): r.on_wheel(E(500,delta=120))
print("clamped left:",r.view_a)
# zoom out all the way
for _ in range(80): r.on_wheel(E(300,delta=-120,state=4))
print("zoom out ->",round(r.zoom_factor(),3),"label:",r.zoom_lbl.config.call_args)
# thumbs at level 3 view
r.start_thumbs(); time.sleep(8); print("level0 thumbs:",len(r.cache),"n1",r.n1)
r.set_view(100,20); r.refresh_thumbs(); time.sleep(8)
lv=r.level(); ks=sorted(k for k in r.cache if k[0]==lv); print("zoom",round(r.zoom_factor(),1),"level",lv,"thumbs at level:",len(ks),"cache",len(r.cache))
step=r.info["dur"]/(r.n1*2**lv); print("covers times",round((ks[0][1]+.5)*step,1),"..",round((ks[-1][1]+.5)*step,1),"(view 100..120 +-25%)")
r.redraw(); print("redraw ok")
# drag trim handle zoomed, with edge scroll
r.start=105; r.end=115
r.on_down(E(r._x(105))); print("grab:",r.drag); r.on_move(E(r._x(106))); print("start ->",round(r.start,2))
r.on_move(E(990)); print("edge: start",round(r.start,2),"view_a",round(r.view_a,2)); r.drag=None
# overview drag
r.on_down(E(100,y=r.canvas.winfo_height() if False else 90)); print("overview drag:",r.drag); r.on_move(E(800,y=90)); print("view_a after overview",round(r.view_a,1)); r.drag=None
# playhead follow
r.set_view(100,20); r.set_playhead_fast(250.0); print("follow: view",round(r.view_a,1),"..",round(r.view_a+r.view_len,1))
# S inside zoomed view
r.playhead=r.view_a+2; r.start=0; r.end=r.info["dur"]; r.add_section(); print("section:",[(round(s["a"],1),round(s["b"],1)) for s in r.sections])
# second row unaffected
print("row2 view",app.rows[1].view_a,app.rows[1].view_len)
