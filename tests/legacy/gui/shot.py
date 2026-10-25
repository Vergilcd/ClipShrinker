import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
src=sys.argv[1]; out=sys.argv[2]; theme=sys.argv[3] if len(sys.argv)>3 else None
sys.path.insert(0, os.path.dirname(src))
spec=importlib.util.spec_from_file_location("vs",src); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
app=vs.App()
app.geometry("1280x860+0+0")
if theme and hasattr(app,"apply_theme"): app.apply_theme(theme, save=False)
app.update()
app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/B_16x9.mp4"])
for i in range(40):
    app.update(); time.sleep(0.1)
if len(app.rows)>1:
    app.rows[0].playhead=1.0; app.set_active(app.rows[0]); app.rows[0].add_section(); app.rows[0].redraw()
for i in range(10): app.update(); time.sleep(0.05)
app.update()
print("thumbs:",[len(r.cache) for r in app.rows])
os.system(f"import -window root {out}")
app.destroy()
