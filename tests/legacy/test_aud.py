import sys, os, subprocess, json, re
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
o=S+"/t/aud_"
A=mk("A_wide.mp4")
a=withsec(A,[(0.5,2.5,4.0),(2.8,3.8,0.5)])   # out: 0.5 normal, 0.5 fast, 0.3 normal, 2.0 slow, 0.2 normal
print("pieces",a["pieces"])
def lv(f,t0,d):
    r=subprocess.run([vs.FFMPEG,"-v","info","-ss",str(t0),"-t",str(d),"-i",f,"-vn","-af","volumedetect","-f","null","-"],capture_output=True,text=True)
    m=re.search(r"max_volume: (\S+)",r.stderr); return m.group(1) if m else None
for fa in vs.FAST_AUDIO:
    for sa in vs.SLOW_AUDIO:
        f=o+"x.mp4"
        run(f"{fa} / {sa}",f,[a],target_mb=0.8,fast_audio=fa,slow_audio=sa,vhs=True)
        # fast section is out 0.5-1.0 ; slow section out 1.3-3.3
        print("   fast start/mid:",lv(f,0.5,0.4),lv(f,0.75,0.2),"| slow start/mid/end:",lv(f,1.3,0.5),lv(f,2.0,0.5),lv(f,2.8,0.5))
