import sys, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0"}
app=vs.App()
for i in range(10): app.update(); time.sleep(0.03)
app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4"])
for i in range(15): app.update(); time.sleep(0.03)
r=app.rows[1]; r.title_lbl.event_generate("<Button-1>", x=5,y=5)
for i in range(10): app.update(); time.sleep(0.03)
print("active after title click:", app.rows.index(app.active_row), [x.tl_h for x in app.rows])
st=vs.ttk.Style(app); print("X fg:", st.lookup("RowX.TButton","foreground"), "font:", st.lookup("RowBtn.TButton","font"))
app.destroy()
