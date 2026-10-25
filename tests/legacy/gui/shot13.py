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
print("vsb empty:",bool(app.list_vsb.winfo_manager()))
app.add_files([S+"/t/A_wide.mp4"]); pump(20)
print("vsb 1 clip:",bool(app.list_vsb.winfo_manager()),app.list_inner.winfo_reqheight(),app.list_canvas.winfo_height(),app.list_canvas.yview())
app.prog["value"]=60; pump(3)
print("bar h",app.prog.winfo_height(),"btn h",app.go_btn.winfo_height())
os.system(f"import -window root -crop 1180x320+0+400 {S}/gui/v86_{th}_a.png")
app.add_files([S+"/t/B_16x9.mp4",S+"/t/C_noaudio.mp4",S+"/t/D_2aud.mp4"]); pump(25)
print("vsb 4 clips:",bool(app.list_vsb.winfo_manager()))
app.show_settings(); pump(15)
os.system(f"import -window root {S}/gui/v86_{th}_s.png")
app.destroy()
