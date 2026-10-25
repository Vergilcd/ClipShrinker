import sys,os,time,subprocess,importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/new")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
vs.FFMPEG=S+"/linux/ffmpeg"; vs.FFPROBE=S+"/linux/ffprobe"
P=S+"/t/K4_2aud.mp4"
os.system("sync; echo 3 > /proc/sys/vm/drop_caches 2>/dev/null")
for label,dur in (("full remainder 35 s",35.0),("first 4 s",4.0),("first 4 s again (cached)",4.0)):
    t=time.time(); w=vs.make_preview_wav(P,5.0,dur,"m"+label[:3],None,True,2); print("WAV %-26s %.2fs"%(label,time.time()-t))
cmd=vs.play_command(P,5.0,35.0,None,960,540,30,False,None,None)
t=time.time(); p=subprocess.Popen(cmd,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL); buf=p.stdout.read(960*540*3); print("time to first video frame (CPU decode, no GPU here): %.2fs"%(time.time()-t)); p.kill()
