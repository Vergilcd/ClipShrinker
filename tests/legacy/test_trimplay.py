import sys, time, importlib.util, os
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.winsound=None
vs.save_settings=lambda d: None; vs.load_settings=lambda: {"geometry":"1280x800+0+0"}
app=vs.App()
odf=app._draw_frame
calls=[0]
def df():
    calls[0]+=1
    odf()
app._draw_frame=df
log=[]; seen=[]; dcount=[0]
def step1():
    app.add_files([S+"/t/LONG_5min.mp4"]); app.after(1500, step2)
def step2():
    r=app.rows[0]; r.start,r.end=60.0,120.0; r.playhead=80.0
    oa=app._deliver
    def dd(gen,buf,t):
        calls.append(1) if False else None
        dcount[0]+=1
        try: oa(gen,buf,t)
        except Exception as ex: log.append(("deliver exc",repr(ex))); raise
    app._deliver=dd
    app.start_playback(); app.after(2000, step3)
def step3():
    r=app.rows[0]; log.append(("playing at", round(r.playhead,1)))
    app.focus_force(); app.update()
    r.playhead=round(r.playhead,1)+0.0
    app.event_generate("<Key-2>"); log.append(("end now", round(r.end,1)))
    def sample():
        seen.append(round(r.playhead,1))
        if len(seen)<40: app.after(100, sample)
        else: finish()
    sample()
def finish():
    log.append(("min/max playhead after end set", min(seen), max(seen), "playing", app.playing))
    log.append(("deliver calls",dcount[0],"pending",app._draw_pending,"draw calls",calls[0],app.play_row.playhead if app.play_row else None, app.status.cget("text"))); app.stop_playback(); app.destroy(); print(log)
app.after(500, step1); app.mainloop()
