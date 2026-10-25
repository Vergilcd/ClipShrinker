import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
vs.load_settings=lambda: {"theme":"dark","geometry":"1180x800+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.03)
pump(10); app.show_shortcuts(); pump(10)
win=[w for w in app.winfo_children() if w.winfo_class()=="Toplevel" and w.title()=="Shortcuts"][0]
def find(w,out):
    for c in w.winfo_children():
        if c.winfo_class()=="TButton": out.append(c)
        find(c,out)
    return out
bs=[b for b in find(win,[]) if b.cget("text") not in ("Close",)]
print([b.cget("text") for b in bs])
kb=bs[4]  # speed key button (trim1,def,trim2,def,speed,def...)
kb.invoke(); pump(3); print("after click:",kb.cget("text"))
win.focus_force(); pump(3)
win.event_generate("<Shift_L>"); win.event_generate("<KeyPress>",keysym="x"); pump(5)
print("after x:",kb.cget("text"), app.keys["speed"], app.hint_act["speed"].cget("text"))
kb.invoke(); pump(2); win.event_generate("<KeyPress>",keysym="Escape"); pump(3); print("escape cancels, win exists:", win.winfo_exists(), kb.cget("text"))
kb.invoke(); pump(2); win.event_generate("<KeyPress>",keysym="d"); pump(3); print("conflict:", kb.cget("text"), app.status.cget("text")[:60]); pump(40); print(kb.cget("text"))
bs[5].invoke(); pump(3); print("default ->", app.keys["speed"], kb.cget("text"))
app.destroy()
