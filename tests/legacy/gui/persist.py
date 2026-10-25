import sys,os,time,json,importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
R=S+"/gui/run1"; sys.path.insert(0,R)
spec=importlib.util.spec_from_file_location("vs",R+"/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
def pump(n=20):
    for _ in range(n): app.update(); time.sleep(0.05)
phase=sys.argv[1]
if phase=="1":
    app=vs.App(); pump()
    print("default geometry:",app.geometry(),"| theme",app.theme,"| codec",app.codec_var.get(),"| size",app.size_var.get())
    app.add_files([S+"/t/A_wide.mp4",S+"/t/D_2aud.mp4",S+"/t/B_16x9.mp4",S+"/t/C_noaudio.mp4"]); pump(40)
    # resize test: grow window height by 200 and compare pane heights
    h0=app.preview_box.winfo_height(); l0=app.list_canvas.winfo_height(); app.geometry("1180x1140+120+60"); pump(20)
    h1=app.preview_box.winfo_height(); l1=app.list_canvas.winfo_height()
    print(f"preview height {h0}->{h1} | clip list height {l0}->{l1}")
    app.geometry("1180x800+120+60"); pump(20)
    print(f"after shrinking: preview {app.preview_box.winfo_height()} list {app.list_canvas.winfo_height()}")
    # change settings
    app.size_var.set("25"); app.bitrate_var.set("12,5"); app.crop_var.set("21:9"); app.codec_var.set("H.264 (h264_nvenc)")
    app.audio_var.set("96k"); app.atrk_var.set("Mix into one track"); app.mutemic_var.set(True); app.hdr_var.set(False)
    app.mode_var.set("join"); app.trans_var.set("Fade through black"); app.tlen_var.set("0.8"); app.sound_var.set(False); app.mute_var.set(False)
    app.theme_var.set(False); app._toggle_theme(); pump()
    app.geometry("1250x850+300+120"); pump(10)
    print("res now:",app.res_var.get(),"| size entry state:",str(app.size_entry.cget("state")))
    app.on_close()
else:
    app=vs.App(); pump(30)
    print("restored geometry:",app.geometry(),"| theme",app.theme)
    print("vars:",app.size_var.get(),app.bitrate_var.get(),app.crop_var.get(),app.codec_var.get(),app.audio_var.get(),app.atrk_var.get(),
          app.mutemic_var.get(),app.hdr_var.get(),app.mode_var.get(),app.trans_var.get(),app.tlen_var.get(),app.sound_var.get(),app.mute_var.get(),"| res",app.res_var.get())
    print("size entry:",str(app.size_entry.cget("state")),"| trans combo:",str(app.trans_cb.cget("state")),"| res values:",app.res_cb.cget("values"))
    print("sash:",app.paned.sashpos(0))
    app.destroy()
