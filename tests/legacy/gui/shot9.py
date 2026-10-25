import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
vs.PRESETS_FILE=S+"/t/presets_shot.json"
import json; json.dump({"presets":[{"name":"Discord","mbps":8,"key":"q"},{"name":"Archive 4K","mbps":40,"key":"w"},{"name":"Quick","mbps":15,"key":""}]},open(vs.PRESETS_FILE,"w"))
vs.load_settings=lambda: {"theme":sys.argv[1],"geometry":"1180x720+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.04)
pump(15)
app.add_files([S+"/t/A_wide.mp4",S+"/t/B_16x9.mp4"]); pump(20)
app.rows[0].set_preset("Archive 4K"); pump(3)
app.prog["value"]=100; app.prog.set_done(True); pump(3)
os.system(f"import -window root {S}/gui/v80_main_{sys.argv[1]}.png")
app.show_presets(); pump(10)
os.system(f"import -window root {S}/gui/v80_presets_{sys.argv[1]}.png")
app._preset_edit(0); pump(10)
os.system(f"import -window root {S}/gui/v80_edit_{sys.argv[1]}.png")
d=vs.ChoiceDialog(app,"Close Clip Shrinker","2 clips are open in the timeline.",["Save","Close without saving","Cancel"],0,2); pump(10)
os.system(f"import -window root {S}/gui/v80_close_{sys.argv[1]}.png"); app.destroy()
