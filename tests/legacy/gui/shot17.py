import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
vs.RIFE_DIR="/tmp/claude-0/rife_test"
vs.load_settings=lambda: {"theme":"dark","geometry":"1180x800+0+0"}
app=vs.App()
import threading, queue
Q=queue.Queue(); _after=app.after; main=threading.current_thread()
def after(ms,fn=None,*a):
    if threading.current_thread() is not main and fn is not None:
        Q.put(fn); return None
    return _after(ms,fn,*a) if fn is not None else _after(ms)
app.after=after
def pump(n):
    for i in range(n):
        app.update(); time.sleep(0.05)
        while not Q.empty(): Q.get()()
pump(10); app.show_settings(); pump(10)
os.system(f"import -window root {S}/gui/v105_a.png")
app.download_rife(); shot=False; t0=time.time()
while app.rife_busy and time.time()-t0<120:
    pump(2)
    if not shot and app.rife_pct>=40:
        pump(2); os.system(f"import -window root {S}/gui/v105_b.png"); shot=True; print("shot at",app.rife_busy)
pump(10); print("end:",app.rife_lbl.cget("text")); os.system(f"import -window root {S}/gui/v105_c.png")
app.destroy()
