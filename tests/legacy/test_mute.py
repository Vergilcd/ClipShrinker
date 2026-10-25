import sys, time, importlib.util, os
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
class API:
    def __init__(s): s.vol=[]; s.writes=[]; s.d=[0]*10
    def open(s,*a): pass
    def pause(s): pass
    def resume(s): pass
    def free(s,i): return True
    def all_free(s): return True
    def write(s,i,data): s.writes.append(any(data))
    def reset(s): pass
    def close(s): pass
    def set_volume(s,v): s.vol.append(v)
import io
a=API(); w=vs.WaveOut(a); w.feed(io.BytesIO(b"\x01"*17640*3)); time.sleep(0.2); print("normal writes non-silent:", a.writes)
a=API(); w=vs.WaveOut(a); w.set_muted(True); w.feed(io.BytesIO(b"\x01"*17640*3)); time.sleep(0.2); print("muted writes non-silent:", a.writes, "volume calls", a.vol)
