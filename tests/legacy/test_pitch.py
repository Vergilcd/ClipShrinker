import sys, os, subprocess, importlib.util, struct
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
exec(open(S+"/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
vs.AUD["slow"]="normal"
for deep in (True,False):
    vs.AUD["deep"]=deep
    parts=["sine=f=440:d=2:r=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[ab]"]+vs.seg_audio("ab",[(0,2,0.5)],0,"w","aout",True,48000)
    r=subprocess.run([vs.FFMPEG,"-v","error","-filter_complex",";".join(parts),"-map","[aout]","-f","f32le","-ac","1","-"],capture_output=True)
    x=struct.unpack(f'{len(r.stdout)//4}f',r.stdout); z=sum(1 for a,b in zip(x,x[1:]) if a<0<=b)
    print("deep" if deep else "stretch","dur",len(x)/48000,"approx Hz",round(z/(len(x)/48000)))
