import sys; sys.path.insert(0,"/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t"); import stubtk
import sys, math, time
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
P=mk("pan.mp4")
t0=time.time()
path=vs.track_zoom(P,(0.0,4.0,0.4,0.3,0.2,True),None)
print("samples",len(path),"time %.2fs"%(time.time()-t0))
for t,dx,dy in path[::8]:
    ex=-(60*t+20*math.sin(t*3))/640; ey=-(25*math.sin(t*1.5))/360
    print(f"{t:5.2f} dx={dx:+.4f} exp={ex:+.4f} | dy={dy:+.4f} exp={ey:+.4f}")
o=S+"/t/tk_"
vs.ZOOM.update(motion=True,len=0.5,sound=False,vol=100)
for name,tr in (("tracked",True),("fixed",False)):
    b=dict(P); b["zooms"]=[(0.5,4.5,0.4,0.3,0.2,tr)]
    run(name,o+name+".mp4",[b],target_mb=3)
    for t in (0.2,1.0,2.0,3.0,4.0):
        subprocess.run([vs.FFMPEG,"-v","error","-y","-ss",str(t),"-i",o+name+".mp4","-frames:v","1","-vf","scale=240:-1",f"{o}{name}_{t}.png"])
