import sys, time, importlib.util, os
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0","theme":sys.argv[1]}
app=vs.App()
def pump(n=10):
    for i in range(n): app.update(); time.sleep(0.03)
pump(); app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/B_16x9.mp4"]); pump(30)
class E: pass
r=app.rows[1]; e=E(); e.x_root=500; e.y_root=app.rows[2].winfo_rooty()+10
r._hdr_move(e); pump(5)
print("ghost:", r._ghost is not None, "geom", r._ghost.geometry() if r._ghost else None, "order", [os.path.basename(x.path)[0] for x in app.rows])
os.system(f"import -window root {S}/gui/ghost_{sys.argv[1]}.png")
r._hdr_up(e); pump(3); print("ghost gone:", r._ghost is None)
app.destroy()
