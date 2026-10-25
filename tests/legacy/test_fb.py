import sys, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0","theme":"light"}
app=vs.App()
def pump(n=10):
    for i in range(n): app.update(); time.sleep(0.03)
pump(); app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/B_16x9.mp4"]); pump(20)
r=app.rows[1]; up,dn,x,fit=r.hbtns[2],r.hbtns[1],r.hbtns[0],r.hbtns[3]
class E:
    def __init__(s): s.x,s.y=3,3
up.command(); pump(); print("order after up:", [vs.os.path.basename(q.path) for q in app.rows])
print("bg normal", x.cget("bg"), x.cget("fg"))
x._set_hover(True); print("hover", x.cget("bg"))
app.apply_theme("dark",save=False); pump(30); print("after dark: ", x.cget("bg"), "card", app.pal["card"])
n=len(app.rows); x.command(); pump(); print("rows", n,"->",len(app.rows))
app.destroy()
