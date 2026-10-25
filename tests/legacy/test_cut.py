import sys, os, subprocess, re
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
A=mk("A_wide.mp4"); a=withsec(A,[(0.5,2.5,0.5)])
for cut in ["x"]:
    for sa in (vs.SLOW_AUDIO[0],vs.SLOW_AUDIO[1],vs.SLOW_AUDIO[2]):
        run(f"cut {cut} / {sa}",S+"/t/cut.mp4",[a],target_mb=0.8,slow_audio=sa,slow_pitch=vs.SLOW_PITCH[0])
