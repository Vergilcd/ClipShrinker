import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
th=sys.argv[1]
vs.load_settings=lambda: {"theme":th,"geometry":"1180x800+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.04)
pump(10)
app.add_files([S+"/t/B_16x9.mp4"]); pump(25)
row=app.rows[0]
class E:
    def __init__(s,k,st=0): s.keysym=k; s.state=st
print("default s ->", bool(app._on_bound_key(E("s"))), len(row.sections))
print("S with shift (not bound yet):", app._on_bound_key(E("S",1)), len(row.sections))
app.show_shortcuts(); pump(10)
win=[w for w in app.winfo_children() if w.winfo_class()=="Toplevel" and w.title()=="Shortcuts"][0]
# find the key button for 'speed': simulate rebind through internals
app.set_key("speed","shift+s"); pump(3)
print("hint:", app.hint_act["speed"].cget("text"))
print("plain s now:", app._on_bound_key(E("s")), "shift+s:", bool(app._on_bound_key(E("S",1))), len(row.sections))
print("conflict:", app.key_conflict("ctrl+s","speed"), "|", app.key_conflict("d","speed"), "|", app.key_conflict("m","zoom"), "|", app.key_conflict("x","speed"))
win.destroy(); app.show_shortcuts(); pump(10)
os.system(f"import -window root {S}/gui/v107_{th}_k.png")
app.show_settings(); pump(10)
os.system(f"import -window root {S}/gui/v107_{th}_s.png")
print("tag", app._name_tag()); app.suffix_var.set(" my/clip: "); print("tag", app._name_tag())
app.codec_var.set("H.264 (h264_nvenc)"); app.suffix_var.set(""); print("tag", app._name_tag())
print("interp default:", app.interp_var.get(), "tlen", app.tlen_var.get())
d=app.save_all_settings(); print(app.settings.get("keys"), app.settings.get("suffix"))
app.destroy()
