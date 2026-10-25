import sys, os, subprocess, json, glob
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
vs.RIFE_DIR=S+"/t/fakerife"
print("rife_ready",vs.rife_ready())
o=S+"/t/rife_"
A=mk("A_wide.mp4"); B=mk("B_16x9.mp4")
a=withsec(A,[(0.5,2.0,0.5),(2.5,3.5,0.25)])
print("pieces",a["pieces"],"D",vs.pieces_duration(a["pieces"]))
run("rife single",o+"1.mp4",[a],target_mb=0.9,slow_interp=vs.SLOW_INTERP[2])
print("frames/dur", info(o+"1.mp4"))
b=withsec(B,[(0.5,2.0,0.5)])
msgs=[]
run("rife join xfade",o+"2.mp4",[a,b],xfade="fade",T=0.4,target_mb=1.2,slow_interp=vs.SLOW_INTERP[2])
print("leftover temp dirs:",glob.glob("/tmp/clipshrinker_rife_*"), glob.glob(tempfile.gettempdir()+"/clipshrinker_rife_*") if 'tempfile' in dir() else "")
# fallback: no rife installed
vs.RIFE_DIR=S+"/t/nonexistent"
run("rife missing -> blend",o+"3.mp4",[a],target_mb=0.9,slow_interp=vs.SLOW_INTERP[2])
# fixed bitrate path
vs.RIFE_DIR=S+"/t/fakerife"
run("rife fixed bitrate",o+"4.mp4",[a],bitrate_mbps=2,slow_interp=vs.SLOW_INTERP[2])
