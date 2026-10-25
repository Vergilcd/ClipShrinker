import sys
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
import subprocess, re
o=S+"/t/zf_"
B=mk("B_16x9.mp4")
def frames(f,times,tag):
    for t in times:
        subprocess.run([vs.FFMPEG,"-v","error","-y","-ss",str(t),"-i",f,"-frames:v","1","-vf","scale=320:-1",f"{o}{tag}_{t}.png"])
def lv(f,t0,d):
    r=subprocess.run([vs.FFMPEG,"-v","info","-ss",str(t0),"-t",str(d),"-i",f,"-vn","-af","volumedetect","-f","null","-"],capture_output=True,text=True)
    m=re.search(r"max_volume: (\S+)",r.stderr); return m.group(1) if m else None
b=dict(B); b["zooms"]=[(0.8,2.4,0.5,0.5,0.5)]
for name,motion in (("motion",True),("instant",False)):
    vs.ZOOM.update(motion=motion,len=0.5,sound=True,vol=100)
    run(name,o+name+".mp4",[b],target_mb=1.5,zoom_fx=vs.ZOOM_FX[0 if motion else 1])
    frames(o+name+".mp4",[0.3,0.95,1.1,1.6,2.35,2.6],name)
    print("  whoosh in :",lv(o+name+".mp4",0.8,0.5),"out:",lv(o+name+".mp4",1.9,0.5),"quiet mid:",lv(o+name+".mp4",1.4,0.3),"before",lv(o+name+".mp4",0.0,0.5))
# with speed section inside + 2 clips joined, different crop
bs=withsec(dict(b),[(1.0,2.0,2.0)]); bs["zooms"]=[(0.8,2.4,0.5,0.5,0.5)]
a=mk("A_wide.mp4")
run("speed+join",o+"sj.mp4",[bs,a],target_mb=1.5,xfade="fade",T=0.4,ratio=16/9)
