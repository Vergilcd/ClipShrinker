import sys,os,importlib.util
S="/tmp/claude-0/-home-claude/9b170778-c35e-5e71-953b-d719fdb932f8/scratchpad"
sys.path.insert(0,S+"/new")
spec=importlib.util.spec_from_file_location("vs",S+"/new/video_shrinker.py"); vs=importlib.util.module_from_spec(spec); spec.loader.exec_module(vs)
app=vs.App(); app.update()
print("box bg",app.preview_box.cget("bg"),"label bg",app.preview.cget("bg"),"size",app.preview_box.winfo_width(),app.preview_box.winfo_height())
print("canvas",app.list_canvas.cget("bg"))
app.destroy()
