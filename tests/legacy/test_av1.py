import sys
sys.path.insert(0,"/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t"); import stubtk
exec(open("/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad/t/test_speed.py").read().split("o=S+\"/t/sp_\"")[0])
print(vs.codec_key("AV1 (av1_nvenc)"),vs.codec_key("HEVC (hevc_nvenc)"),vs.codec_key("H.264 (h264_nvenc)"),vs.codec_short("AV1 (av1_nvenc)"))
print(vs.video_codec_args("av1_nvenc",5_000_000))
print("supported here (no GPU expected False):",vs.av1_supported())
