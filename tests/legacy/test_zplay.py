import sys
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
import subprocess
vs.ZOOM["play"]=[(1.0,2.5,0.5,0.5,0.5)]; vs.ZOOM.update(motion=True,len=0.5,sound=True,vol=100)
for crop,pcs in ((None,None),((960,720),None),(None,[(0.5,3.0,1.0)]),(None,vs.build_pieces(0.0,3.0,[{"a":1.0,"b":2.0,"speed":2.0}]))):
    cmd=vs.play_command(S+"/t/B_16x9.mp4",0.0,3.0,crop,640,360 if not crop else 480,30,0,None,pcs)
    r=subprocess.run(cmd,capture_output=True)
    w,h=(640,360) if not crop else (640,480)
    print("crop",crop,"pieces",bool(pcs),"rc",r.returncode,"bytes",len(r.stdout),"frames",len(r.stdout)/(w*h*3),r.stderr[-120:])
a=vs.preview_audio_cmd(S+"/t/B_16x9.mp4",0.0,3.0,None,True,1,S+"/t/zp.wav")
r=subprocess.run(a,capture_output=True); print("audio rc",r.returncode,r.stderr[-150:])
