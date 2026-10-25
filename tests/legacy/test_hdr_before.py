import sys, os, types, subprocess, importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
exec(open(S+"/t/test_play.py").read().split("app=vs.App()")[0])
vs.video_codec_args=lambda codec,v_bps:["-c:v","mpeg4","-b:v","3000k","-pix_fmt","yuv420p"]
i=vs.probe(S+"/t/HDR_wide.mkv"); print("probe keys:", sorted(i))
clip={"path":S+"/t/HDR_wide.mkv","start":0,"end":2,"w":i["w"],"h":i["h"],"fps":i["fps"],"audio":True,"bitrate":0}
st={"target_mb":5,"res_mode":"100%","codec":"h264_nvenc","audio_k":"128k","ratio":None,"xfade":None,"T":0.5}
enc=vs.Encoder(); enc.use_hwdec=False; enc.encode(S+"/t/hdr_out_before.mp4",[clip],st)
r=subprocess.run([S+"/linux/ffprobe","-v","error","-select_streams","v:0","-show_entries","stream=pix_fmt,color_space,color_transfer,color_primaries","-of","default=nw=1",S+"/t/hdr_out_before.mp4"],capture_output=True,text=True); print("OUTPUT tags (current pipeline):\n"+r.stdout)
