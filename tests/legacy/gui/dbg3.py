import sys, os, time, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/pkg082/ClipShrinker/library")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"; vs.save_settings=lambda d: None
vs.load_settings=lambda: {"theme":"dark","geometry":"1180x800+0+0"}
app=vs.App()
def pump(n):
    for i in range(n): app.update(); time.sleep(0.04)
pump(10); app.add_files([S+"/t/B_16x9.mp4"]); pump(25)
print(app._preview_busy, app._pending_preview, app._last_preview is not None)
app.request_preview(app.rows[0],1.0); pump(40)
print(app._preview_busy, repr(str(app.preview.cget("image"))))
print(app._pv_dims())
r=app.rows[0]; i=r.info
cw,ch=vs.crop_size(i["w"],i["h"],app._ratio()); print("crop",cw,ch,i["w"],i["h"], app._ratio())
d=vs.grab_frame(r.path,1.0,1164,360,(cw,ch) if (cw,ch)!=(i["w"],i["h"]) else None,None)
print("grab", None if d is None else len(d))
try:
    app._show_preview(d); pump(5); print(repr(str(app.preview.cget("image"))))
except Exception as ex: print("ERR",ex)
