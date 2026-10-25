import sys, os, time, importlib.util, tempfile, threading, queue, glob
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
vs.video_codec_args=lambda codec,v_bps:["-c:v","mpeg4","-b:v",f"{max(20,int(v_bps/1000))}k","-pix_fmt","yuv420p"]
th=sys.argv[1]
out=tempfile.mkdtemp(prefix="exp_")
vs.load_settings=lambda: {"theme":th,"geometry":"1180x800+0+0","save_dir":out,"suffix":""}
app=vs.App()
Q=queue.Queue(); _after=app.after; main=threading.current_thread()
def after(ms,fn=None,*a):
    if threading.current_thread() is not main and fn is not None:
        Q.put((fn,a)); return None
    return _after(ms,fn,*a) if fn is not None else _after(ms)
app.after=after
def pump(n):
    for i in range(n):
        app.update(); time.sleep(0.04)
        while not Q.empty():
            f_,a_=Q.get(); f_(*a_)
def boom(*a,**k): raise SystemExit("dialog was opened!")
vs.filedialog.asksaveasfilename=boom; vs.filedialog.askdirectory=boom
vs.messagebox.showinfo=lambda *a,**k: None
pump(10); app.add_files([S+"/t/B_16x9.mp4"]); pump(25)
app.size_var.set("1.5"); app.start_export()
t0=time.time()
while time.time()-t0<90 and not glob.glob(out+"/*.mp4"): pump(5)
pump(40)
print(os.listdir(out))
app.start_export() if not app.encoding else None
t0=time.time()
while time.time()-t0<60 and len(glob.glob(out+"/*.mp4"))<2: pump(5)
pump(20); print(sorted(os.listdir(out)))
app.show_settings(); pump(10); os.system(f"import -window root {S}/gui/v108_{th}_s.png")
app.destroy()
