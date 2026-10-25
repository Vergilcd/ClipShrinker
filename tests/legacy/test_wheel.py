import sys
sys.path.insert(0,"/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t"); import stubtk
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
tag=f"cp{sys.version_info[0]}{sys.version_info[1]}"
def w(v,fn): return {"filename":fn,"packagetype":"bdist_wheel","url":"u/"+fn,"size":1}
info={"releases":{"2.5.0":[w(0,f"numpy-2.5.0-{tag}-{tag}-manylinux_2_28_x86_64.whl"),w(0,f"numpy-2.5.0-{tag}-{tag}-win_amd64.whl"),w(0,f"numpy-2.5.0-{tag}-{tag}-manylinux_2_27_aarch64.whl")],
"2.6.0rc1":[w(0,f"numpy-2.6.0rc1-{tag}-{tag}-win_amd64.whl")],"2.4.0":[w(0,"numpy-2.4.0-cp99-cp99-win_amd64.whl")]}}
print(vs._wheel_url(info))
vs.os.name="nt"; print(vs._wheel_url(info))
