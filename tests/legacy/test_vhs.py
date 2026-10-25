import sys, os, subprocess, json
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
vs.VHS["on"]=True
o=S+"/t/vhs_"
A=mk("A_wide.mp4"); B=mk("B_16x9.mp4")
a=withsec(A,[(0.5,2.5,4.0)])
print("pieces",a["pieces"],"D",vs.pieces_duration(a["pieces"]))
run("vhs single",o+"1.mp4",[a],target_mb=0.8)
run("vhs single 20x muted-off",o+"2.mp4",[withsec(A,[(0,3,10.0),(3.2,3.6,0.5)])],target_mb=0.8,fast_audio="Normal sped-up sound")
b=withsec(B,[(0.5,2.5,3.0)])
run("vhs join xfade",o+"3.mp4",[a,b],xfade="fade",T=0.4,target_mb=1.0)
cmd=vs.play_command(A["path"],0.0,4.0,(640,360),320,180,30,False,None,a["pieces"])
r=subprocess.run(cmd,capture_output=True); print("play frames",len(r.stdout)/(320*180*3), r.stderr[-300:])
w=vs.make_preview_wav(A["path"],0.0,A["end"],"x",a["pieces"]); print("wav",os.path.getsize(w) if w else None)
# pick frames: normal part, burst and middle
for t,n in ((0.1,"pre"),(1.0,"burst"),(1.4,"mid"),(1.9,"end")):
    subprocess.run([vs.FFMPEG,"-y","-v","error","-ss",str(t),"-i",o+"1.mp4","-frames:v","1",f"{S}/t/vhs_f_{n}.png"])
