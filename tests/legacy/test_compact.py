import sys, time
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
import importlib.util
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.03)
pump(10); app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/B_16x9.mp4"]); pump(20)
print("active:", app.rows.index(app.active_row), [r.tl_h for r in app.rows])
class E: 
    def __init__(s,x,y): s.x,s.y=x,y
r=app.rows[1]; r.on_down(E(300,20)); r.on_move(E(400,20)); pump(20)
print("after click on strip 2 -> active:", app.rows.index(app.active_row), [r.tl_h for r in app.rows], "playhead", round(r.playhead,2), "drag", r.drag, "start/end", r.start, round(r.end,2))
print("tl: handles not moved:", r.start==0)
app.destroy()
