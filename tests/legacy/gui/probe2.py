import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.save_settings=lambda d: None
vs.load_settings=lambda: {"theme":"dark","geometry":"1180x700+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.04)
pump(10); app.show_settings(); pump(10)
from tkinter import ttk
l=ttk.Label(app.settings_win,text="TEST HEADING",style="Section.TLabel"); l.place(x=5,y=5)
def walk(w):
    for c in w.winfo_children():
        try:
            if str(c.cget("style"))=="Section.TLabel": c.configure(style="Section.TLabel")
        except Exception: pass
        walk(c)
walk(app.settings_win)
pump(10)
os.system(f"import -window root -crop 700x120+300+40 {S}/gui/p2.png")
