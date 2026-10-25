import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
src=sys.argv[1]; out=sys.argv[2]; theme=sys.argv[3]; mode=sys.argv[4]
sys.path.insert(0, os.path.dirname(src))
spec=importlib.util.spec_from_file_location("vs",src); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
vs.save_settings=lambda d: None
vs.load_settings=lambda: {"theme":theme,"geometry":"1280x860+0+0","crop":"16:9"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.05)
pump(20)
print("alpha:",app.attributes("-alpha"))
if mode=="clips":
    app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/LONG_5min.mp4"]); pump(30)
    r=app.rows[0]; r.playhead=1.0; app.set_active(r); r.add_section(); r.playhead=2.6; r.redraw(); r.set_playhead_fast(2.6)
    r2=app.rows[2]; r2.set_view(60,40); r2.playhead=75; r2.set_playhead_fast(75)
    app.pct_lbl.config(text="42%"); app.prog.config(value=42)
if mode=="help": app.show_shortcuts()
pump(12)
os.system(f"import -window root {out}")
app.destroy()
