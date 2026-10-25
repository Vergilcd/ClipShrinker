import sys, time, importlib.util, os, threading
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.winsound=None
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0"}
app=vs.App()
def pump(n=10):
    for i in range(n): app.update(); time.sleep(0.03)
pump(); app.add_files([S+"/t/LONG_5min.mp4"]); pump(30)
r=app.rows[0]; r.start,r.end=60.0,120.0; r.playhead=80.0
ts=[]; o=app._deliver
def d(gen,buf,t): ts.append((time.time(),t))
app._deliver=d
app.start_playback(); time.sleep(1.5)
print("before: last t", round(ts[-1][1],1))
r.playhead=83.0; app._on_trim("end"); print("end ->", r.end)
time.sleep(4.5)
vals=[round(t,1) for _,t in ts]
print("max t seen:", max(vals), " min after peak:", min(vals[vals.index(max(vals)):]))
print("t samples:", vals[::15])
app.stop_playback(); app.destroy()
