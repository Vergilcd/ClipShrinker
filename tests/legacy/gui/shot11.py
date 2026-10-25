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
pump(15)
os.system(f"import -window root {S}/gui/v85_{th}_empty.png")
app.add_files([S+"/t/A_wide.mp4",S+"/t/A_wide.mp4"]); pump(20)
app._project_menu(); pump(10)
os.system(f"import -window root {S}/gui/v85_{th}_menu.png")
app._popup.close(); pump(5)
app.show_settings(); pump(15)
os.system(f"import -window root {S}/gui/v85_{th}_settings.png")
app.destroy()
