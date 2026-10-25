import sys
sys.path.insert(0,"/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/new")
import video_shrinker as vs
vs.AUD.update(fast="normal",slow="normal",fast_vol=50,slow_vol=0)
for l in vs.seg_audio("s",[(0,0.5,1.0),(0.5,2.5,4.0),(2.5,2.8,1.0),(2.8,3.8,0.5)],0,"t","o",True): print(l)
