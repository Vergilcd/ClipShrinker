import sys
sys.path.insert(0,"/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t"); import stubtk
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
import numpy as np
# object leaves: texture shifts left 8 px/frame
rng=np.random.default_rng(3); big=rng.integers(0,255,(180,1200)).astype(np.uint8)
fr=np.stack([big[:, 8*k:8*k+320] for k in range(40)])
mv=vs.track_frames(np,fr,0.3,0.3,0.2)
print([round(m[0]*320,1) for m in mv][::3])
# playback command with tracked zoom + two clips in a graph
P=mk("pan.mp4")
b=dict(P); b["zooms"]=[(0.5,4.5,0.4,0.3,0.2,True)]
b["zooms"]=vs.resolve_tracks(b["zooms"],b,None)
b2=dict(b)
g,vl,al=vs.build_graph([b,b2],640,360,None,"fade",0.4,30)
print(g.count("sendcmd"), "crop@zc0" in g, "crop@zc1" in g)
vs.ZOOM["play"]=b["zooms"]
cmd=vs.play_command(P["path"],0,5,None,640,360,30,0,None,None)
print(" ".join(cmd)[:300])
import subprocess
r=subprocess.run(cmd,capture_output=True); print(r.returncode,len(r.stdout)/(640*360*3),r.stderr[-200:])
