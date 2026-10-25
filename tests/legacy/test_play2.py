import sys, os, types, time, importlib.util
from unittest.mock import MagicMock
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
src=open(S+"/t/test_play.py").read().split("app=vs.App()")[0]
exec(src)
calls=[]
fake=types.SimpleNamespace(SND_FILENAME=1,SND_ASYNC=2,SND_PURGE=4,PlaySound=lambda f,fl: calls.append((os.path.basename(f) if f else None, fl)))
vs.winsound=fake
app=vs.App()
app.after=lambda ms,fn=None,*a: (fn(*a) if callable(fn) and ms==0 else None)
app.add_files([S+"/t/A_wide.mp4"])
row=app.rows[0]; row.start,row.end=0.5,2.0; row.playhead=1.5          # 4 s clip, trimmed 0.5-2.0, selection at 1.5
starts=[]; frames=[]
orig=app._deliver
def d(gen,buf,t,first,wav):
    frames.append(t)
    if first: starts.append(round(t,2))
    orig(gen,buf,t,first,wav)
app._deliver=d
app.start_playback(); time.sleep(3.6)
print("pass start times:", starts, "(expected first = 1.5, then 0.5 repeating)")
print("first pass length: frames until reset:", next(i for i in range(1,len(frames)) if frames[i]<frames[i-1]), "(expected ~15 = 0.5 s*30fps)")
print("audio calls:", [(c[0], c[1]) for c in calls if c[0]])
# pause + resume continues from the playhead
app.stop_playback(); ph=row.playhead; frames.clear(); starts.clear()
app.start_playback(); time.sleep(0.8)
print(f"paused at {ph:.2f}s -> resumed at {starts[0]} (matches: {abs(starts[0]-ph)<0.1})")
# playhead outside the range -> starts at the start handle
app.stop_playback(); row.playhead=3.5; starts.clear(); app.start_playback(); time.sleep(0.6)
print("playhead outside range -> starts at", starts[0], "(expected 0.5)")
app.stop_playback(); app.on_close()
