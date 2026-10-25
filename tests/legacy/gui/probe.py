import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.save_settings=lambda d: None
vs.load_settings=lambda: {"theme":"dark"}
app=vs.App(); app.update()
from tkinter import ttk
st=ttk.Style()
print(st.lookup("Section.TLabel","foreground"), st.lookup("Section.TLabel","font"), app.pal["accent_text"])
def walk(w,d=0):
    for c in w.winfo_children():
        try:
            if c.cget("text") in ("SIZE & QUALITY","AUDIO"): print(c, c.cget("style"), c.cget("foreground"), c.cget("font"))
        except Exception: pass
        walk(c)
walk(app.settings_win)
