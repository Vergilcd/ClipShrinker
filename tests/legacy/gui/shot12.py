import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
th=sys.argv[1]
vs.load_settings=lambda: {"theme":th,"geometry":"1180x720+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.04)
pump(10)
app.add_files([S+"/t/A_wide.mp4"]); pump(20)
app.list_canvas.yview_scroll(-5,"units"); pump(5)
os.system(f"import -window root -crop 1180x300+0+400 {S}/gui/v85b_{th}_1.png")
print("yview",app.list_canvas.yview())
app.add_files([S+"/t/B_16x9.mp4",S+"/t/C_noaudio.mp4"]); pump(20)
app.rows[1].set_selected(True); pump(5)
os.system(f"import -window root -crop 1180x300+0+400 {S}/gui/v85b_{th}_3.png")
app.destroy()
