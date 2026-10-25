import sys, os, types, subprocess, json
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
try:
    import tkinter
except Exception:
    tk=types.ModuleType('tkinter'); tk.Tk=object; sys.modules['tkinter']=tk
    for n in ('filedialog','messagebox','ttk'):
        m=types.ModuleType('tkinter.'+n); sys.modules['tkinter.'+n]=m; setattr(tk,n,m)
    tk.ttk.Frame=object
import importlib.util
spec=importlib.util.spec_from_file_location("vs", S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec)
class Dummy: pass
spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
vs.video_codec_args=lambda codec,v_bps:["-c:v","mpeg4","-b:v",f"{max(20,int(v_bps/1000))}k","-maxrate",f"{max(20,int(v_bps/1000))}k","-bufsize",f"{max(20,int(v_bps/1000))*2}k","-pix_fmt","yuv420p"]

def mk(name, start=0, end=None):
    i=vs.probe(f"{S}/t/{name}")
    return {"path":f"{S}/t/{name}","start":start,"end":end if end else i["dur"],"w":i["w"],"h":i["h"],"fps":i["fps"],"audio":i["audio"],"bitrate":i["bitrate"]}

def info(p):
    r=subprocess.run([vs.FFPROBE,"-v","error","-print_format","json","-show_streams","-show_format",p],capture_output=True,text=True)
    j=json.loads(r.stdout); v=[s for s in j["streams"] if s["codec_type"]=="video"][0]
    a=[s for s in j["streams"] if s["codec_type"]=="audio"]
    return f'{v["width"]}x{v["height"]} fps={v["avg_frame_rate"]} dur={float(j["format"]["duration"]):.2f}s size={os.path.getsize(p)/1048576:.3f}MB audio={"yes" if a else "no"}'

def run(label, out, clips, **kw):
    st={"target_mb":1.0,"res_mode":"Auto","codec":"h264_nvenc","audio_k":"128k","ratio":None,"xfade":None,"T":0.5}
    st.update(kw)
    log=[]
    enc=vs.Encoder(lambda p:None, lambda s: log.append(s) if "pass" in s and "%" not in s else None)
    size=enc.encode(out, clips, st)
    print(f"{label}: OK  limit={st['target_mb']}MB -> {info(out)}  passes={len(log)} last='{log[-1]}'")
    assert size<=st["target_mb"]*1048576

o=S+"/t/out_"
try:
    run("single crop 21:9 (32:9 src)", o+"1.mp4",[mk("A_wide.mp4",0.5,3.5)],ratio=21/9,target_mb=0.6)
    run("single no-crop 16:9 + 21:9 (should not crop)", o+"2.mp4",[mk("B_16x9.mp4")],ratio=21/9,target_mb=0.8)
    for name,x in [("fade","fade"),("smoothright","smoothright"),("slideleft","slideleft"),("fadeblack","fadeblack"),("hardcut",None)]:
        run(f"join3 {name}", o+f"j_{name}.mp4",[mk("A_wide.mp4",0,3),mk("B_16x9.mp4",0,2.5),mk("C_noaudio.mp4",0,2)],ratio=16/9,xfade=x,T=0.6,target_mb=1.5)
    run("join2 big T clamp", o+"j_clamp.mp4",[mk("A_wide.mp4",0,1.0),mk("C_noaudio.mp4",0,1.0)],xfade="fade",T=3.0,target_mb=0.5)
    run("join no audio at all", o+"j_noaud.mp4",[mk("C_noaudio.mp4",0,2),mk("C_noaudio.mp4",1,3)],xfade="slideright",T=0.5,target_mb=0.4)
except Exception as e:
    import traceback; traceback.print_exc(); sys.exit(1)
