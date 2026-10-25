import sys, time, importlib.util, os
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.winsound=None
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0"}
app=vs.App()
def pump(n=10):
    for i in range(n): app.update(); time.sleep(0.03)
pump(); app.add_files([S+"/t/LONG_5min.mp4"]); pump(30)
r=app.rows[0]; app.update(); app.focus_force(); pump(5)
r.playhead=60.0
app.event_generate("<Key-1>"); pump(3); print("start:", r.start, app.status.cget("text"))
r.playhead=120.0
app.event_generate("<Key-2>"); pump(3); print("end:", r.end)
r.playhead=130; app.event_generate("<Key-1>"); pump(3); print("start refused (after end):", r.start, "|", app.status.cget("text"))
r.playhead=10; app.event_generate("<Key-2>"); pump(3); print("end refused (before start):", r.end)
print("count label:", app.count_lbl.cget("text"))
r.start,r.end=60.0,120.0; r.playhead=80.0
app.start_playback(); pump(100); print("status:", app.status.cget("text"), "lvl", app._play_lvl, "proc", app.play_proc and app.play_proc.poll())
print("playing, playhead", round(r.playhead,1), "playing", app.playing)
r.playhead=r.playhead; cur=r.playhead
app.event_generate("<Key-2>"); pump(3); print("end set while playing to", round(r.end,1))
mn=999
for i in range(120):
    app.update(); time.sleep(0.03); mn=min(mn,r.playhead)
print("after end set: playhead now", round(r.playhead,1), "min seen", round(mn,1), "(restart at start 60 expected) playing", app.playing)
app.stop_playback(); app.destroy()
