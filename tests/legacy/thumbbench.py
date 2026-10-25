import sys, time, importlib.util, os, base64
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
for name,dur in (("K4_2aud.mp4",40),("LONG_5min.mp4",300),("A_wide.mp4",4)):
    path=S+"/t/"+name; n=12
    times=[(k+0.5)*dur/n for k in range(n)]
    t=time.time(); old=[vs.grab_frame(path,x,106,60,None,None) for x in times]; t_old=time.time()-t
    got=[]; t=time.time(); c=vs.grab_frames(path,times,106,60,None,lambda: False,lambda i,d: got.append((i,d))); t_new=time.time()-t
    sizes=set()
    import struct
    for i,d in got:
        raw=base64.b64decode(d); sizes.add(struct.unpack(">II",raw[16:24]))
    print(f"{name}: old per-frame {t_old:.2f}s ({sum(1 for o in old if o)} ok) | one process {t_new:.2f}s frames={c} sizes={sizes} order={[i for i,_ in got]==list(range(n))}")
