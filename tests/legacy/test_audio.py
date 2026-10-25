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
def mk2(name,s=0,e=None):
    c=mk(name,s,e); i=vs.probe(f"{S}/t/{name}"); c["naudio"]=i["naudio"]; return c
def streams(p):
    r=subprocess.run([vs.FFPROBE,"-v","error","-print_format","json","-show_streams",p],capture_output=True,text=True)
    return [(s["codec_type"],s.get("tags",{}).get("handler_name")) for s in json.loads(r.stdout)["streams"]]
def vol(p,k):
    r=subprocess.run([vs.FFMPEG,"-v","info","-i",p,"-map",f"0:a:{k}","-af","volumedetect","-f","null","-"],capture_output=True,text=True)
    import re; m=re.search(r"mean_volume: (-?[\d.inf]+)",r.stderr); return m.group(1) if m else None
o=S+"/t/au_"
D=mk2("D_2aud.mp4"); A=mk2("A_wide.mp4"); C=mk2("C_noaudio.mp4")
print("probe naudio D/A/C:",D["naudio"],A["naudio"],C["naudio"])
run("D separate",o+"1.mp4",[D],target_mb=0.8); print(" streams",streams(o+"1.mp4"),"vol t0/t1",vol(o+"1.mp4",0),vol(o+"1.mp4",1))
run("D mix",o+"2.mp4",[D],target_mb=0.8,mix_audio=True); print(" streams",streams(o+"2.mp4"),"vol",vol(o+"2.mp4",0))
d2=withsec(D,[(1,3,4.0)])
run("D speed",o+"3.mp4",[d2],target_mb=0.8); print(" streams",streams(o+"3.mp4"))
for xf in (None,"fade"):
    run(f"join D,A,C xfade={xf}",o+f"4{xf}.mp4",[D,A,C],xfade=xf,T=0.4,target_mb=1.5); print(" streams",streams(o+f"4{xf}.mp4"),"mic vol",vol(o+f"4{xf}.mp4",1))
run("join mixed",o+"5.mp4",[D,A],xfade="fade",T=0.4,target_mb=1.5,mix_audio=True); print(" streams",streams(o+"5.mp4"))
w=vs.make_preview_wav(D["path"],0,4.0,"y",None,True,2); print("wav2",w and os.path.getsize(w))
w=vs.make_preview_wav(D["path"],0,4.0,"z",d2["pieces"],True,2); print("wav2+seg",w and os.path.getsize(w))
w=vs.make_preview_wav(A["path"],0,4.0,"q",None,True,1); print("wav1",w and os.path.getsize(w))
