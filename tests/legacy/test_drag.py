import sys, time, importlib.util, os
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0"}
app=vs.App()
def pump(n=10):
    for i in range(n): app.update(); time.sleep(0.03)
pump(); app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/B_16x9.mp4",S+"/t/C_noaudio.mp4"]); pump(25)
names=lambda: [os.path.basename(r.path)[:1] for r in app.rows]
print("start", names(), "active", app.rows.index(app.active_row))
class E: pass
r=app.rows[0]
# drag first row (active, big) down in steps to below the last row
for target in range(3):
    last=app.rows[-1]; y=last.winfo_rooty()+last.winfo_height()-2
    e=E(); e.y_root=y
    r._hdr_move(e); pump(3)
print("after drag to bottom", names(), "y ascending:", [x.winfo_y() for x in app.rows]==sorted(x.winfo_y() for x in app.rows), [x.winfo_y() for x in app.rows])
first=app.rows[0]; e=E(); e.y_root=first.winfo_rooty()+2
r._hdr_move(e); pump(3); print("drag back to top", names())
# stability: repeated same pointer position doesn't oscillate
n0=names(); [r._hdr_move(e) or pump(1) for _ in range(5)]; print("stable:", n0==names())
r._hdr_up(e); print("titles:", [x.title_lbl.cget("text")[:12] for x in app.rows])
app.destroy()
