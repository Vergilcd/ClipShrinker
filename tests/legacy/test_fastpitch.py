import sys, struct
sys.path.insert(0,"/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t"); import stubtk
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
import subprocess
vs.AUD["fast"]="normal"
for high in (False,True):
  for sp in (2.0,4.0,100.0):
    vs.AUD["high"]=high
    parts=["sine=f=440:d=4:r=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[ab]"]+vs.seg_audio("ab",[(0,4,sp)],0,"w","aout",True,48000)
    r=subprocess.run([vs.FFMPEG,"-v","error","-filter_complex",";".join(parts),"-map","[aout]","-f","f32le","-ac","1","-"],capture_output=True)
    x=struct.unpack(f'{len(r.stdout)//4}f',r.stdout); z=sum(1 for a,b in zip(x,x[1:]) if a<0<=b)
    d=len(x)/48000
    print("high" if high else "same","speed",sp,"dur %.3f"%d,"approx Hz",round(z/d) if d else None)
