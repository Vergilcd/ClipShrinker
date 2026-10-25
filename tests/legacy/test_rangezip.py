import sys, time, zipfile, os, shutil
sys.path.insert(0,"/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t"); import stubtk
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
t0=time.time(); rf=vs.RangeFile(vs.RIFE_URL)
xd=S+"/rifedl/y"; shutil.rmtree(xd,ignore_errors=True)
with zipfile.ZipFile(rf) as zf:
    keep=[m for m in zf.namelist() if any(m.lower().split("/",1)[-1].startswith(k) for k in vs.RIFE_KEEP) and not m.endswith("/")]
    print(keep)
    for m in keep: zf.extract(m,xd)
print("fetched %.1f MB in %.1fs"%(rf.got/1048576,time.time()-t0))
vs.RIFE_DIR=xd+"/rife-ncnn-vulkan-20221029-windows"
print("ready (exe name is windows-only):", vs.rife_model())
