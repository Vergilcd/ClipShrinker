import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
vs.load_settings=lambda: {"theme":"dark","geometry":"1280x860+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.04)
pump(15); app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/B_16x9.mp4",S+"/t/A_wide.mp4"]); pump(25)
r=app.rows[0]; r.playhead=1.0; r.add_section(); r.playhead=3.0; r.add_section()
class E: state=0x1; x=10; y=10
app.rows[2]._hdr_down(E())
r.sel=r.sections[0]; r.redraw(); pump(10)
os.system(f"import -window root {S}/gui/v55.png"); app.destroy()
