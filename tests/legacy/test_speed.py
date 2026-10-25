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
    return {"path":f"{S}/t/{name}","start":start,"end":end if end else i["dur"],"w":i["w"],"h":i["h"],"fps":i["fps"],"audio":i["audio"],"bitrate":i["bitrate"],"trc":None}

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


def withsec(c, secs):
    c=dict(c); c["pieces"]=vs.build_pieces(c["start"],c["end"],[{"a":a,"b":b,"speed":s} for a,b,s in secs]); return c
o=S+"/t/sp_"
A=mk("A_wide.mp4"); B=mk("B_16x9.mp4"); C=mk("C_noaudio.mp4")
print("A dur",A["end"],"B dur",B["end"],"C dur",C["end"])
a=withsec(A,[(0.5,1.5,4.0),(2.0,2.5,0.5)])
print("pieces",a["pieces"],"dur",vs.pieces_duration(a["pieces"]))
run("single 2 sections",o+"1.mp4",[a],target_mb=0.6)
print("expected dur",vs.pieces_duration(a["pieces"]))
run("single, crop, sections + 20x (atempo chain)",o+"2.mp4",[withsec(A,[(0,2,20.0)])],ratio=21/9,target_mb=0.6)
print("expected", vs.pieces_duration(withsec(A,[(0,2,20.0)])["pieces"]))
b=withsec(B,[(1,2,2.0)]); c=withsec(C,[(0.2,1.0,3.0)])
for xf in (None,"fade"):
    run(f"join A,B,C sections xfade={xf}",o+f"j{xf}.mp4",[a,b,c],xfade=xf,T=0.4,target_mb=1.0)
    print("expected", sum(vs.pieces_duration(x["pieces"]) for x in (a,b,c)) - (0.8 if xf else 0))
cmd=vs.play_command(A["path"],0.0,3.0,(640,360),320,180,30,False,None,a["pieces"])
r=subprocess.run(cmd,capture_output=True); print("play frames",len(r.stdout)/(320*180*3), r.stderr[-200:])
w=vs.make_preview_wav(A["path"],0.0,A["end"],"x",a["pieces"]); print("wav",w, os.path.getsize(w) if w else None)
