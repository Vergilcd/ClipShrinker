import sys, os, subprocess, json
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
o=S+"/t/slow_"
A=mk("A_wide.mp4"); B=mk("B_16x9.mp4")
for blend in (True,False):
    vs.SLOW["blend"]=blend
    a=withsec(A,[(0.5,2.0,0.5)])
    print("pieces",a["pieces"],"D",vs.pieces_duration(a["pieces"]))
    run(f"slow blend={blend}",o+f"{int(blend)}.mp4",[a],target_mb=0.8)
vs.SLOW["blend"]=True
vs.VHS["on"]=True; vs.AUD["fast"]="fx"
a=withsec(A,[(0.2,1.0,4.0),(1.5,2.5,0.25)])
run("mixed fast+slow, vhs video only",o+"m.mp4",[a],target_mb=0.8)
b=withsec(B,[(0.5,2.0,0.5)])
run("join xfade slow",o+"j.mp4",[a,b],xfade="fade",T=0.4,target_mb=1.0)
cmd=vs.play_command(A["path"],0.0,4.0,(640,360),320,180,30,False,None,a["pieces"])
r=subprocess.run(cmd,capture_output=True); print("play frames",len(r.stdout)/(320*180*3), r.stderr[-300:])
w=vs.make_preview_wav(A["path"],0.0,A["end"],"x",a["pieces"]); print("wav",os.path.getsize(w) if w else None)
