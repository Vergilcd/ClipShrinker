import sys, os, subprocess, json, glob
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
vs.RIFE_DIR=S+"/t/fakerife"
orig=vs.Encoder._rife_prepare
def spy(self,*a,**k):
    r=orig(self,*a,**k); print("  rife_map",self.rife_map,"extras",[(os.path.basename(d),nf,round(D,3)) for d,nf,D in self.extras],"-> progress",r); return r
vs.Encoder._rife_prepare=spy
o=S+"/t/rife_"
A=mk("A_wide.mp4")
a=withsec(A,[(0.5,2.0,0.5),(2.5,3.5,0.25)])
st={"target_mb":1.0,"bitrate_mbps":2,"res_mode":"Auto","codec":"h264_nvenc","audio_k":"128k","ratio":None,"xfade":None,"T":0.5,"slow_interp":vs.SLOW_INTERP[2]}
enc=vs.Encoder(lambda p:None, lambda s: print("  status:",s) if "RIFE" in s else None)
print("size",enc.encode(o+"4.mp4",[a],st), info(o+"4.mp4"))
st["target_mb"]=1.0; st.pop("bitrate_mbps")
enc=vs.Encoder(lambda p:None, lambda s: None)
print("size",enc.encode(o+"5.mp4",[a],st), info(o+"5.mp4"))
