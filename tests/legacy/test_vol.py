import sys, os, subprocess, json, re
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
o=S+"/t/vol_"
A=mk("A_wide.mp4")
a=withsec(A,[(0.5,2.5,4.0),(2.8,3.8,0.5)])
def lv(f,t0,d):
    r=subprocess.run([vs.FFMPEG,"-v","info","-ss",str(t0),"-t",str(d),"-i",f,"-vn","-af","volumedetect","-f","null","-"],capture_output=True,text=True)
    m=re.search(r"max_volume: (\S+)",r.stderr); return m.group(1) if m else None
for fv,sv in ((100,100),(50,200),(0,0)):
    f=o+"x.mp4"
    run(f"vol {fv}/{sv}",f,[a],target_mb=0.8,fast_audio="Normal sped-up sound",slow_audio="Normal sound",vhs=False,fast_vol=fv,slow_vol=sv)
    print("   fast:",lv(f,0.5,0.15),"slow:",lv(f,1.5,0.3),lv(f,2.5,0.3), "normal:",lv(f,0.0,0.4))
