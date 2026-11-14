"""Phase 2 end-to-end check: every project in tests/media/projects exported by Python 1.0.8 and by the C# CLI.

    ..\\Dev\\library\\python\\python.exe -I tools\\compare_exports.py [project names...]

Compares the ffmpeg / RIFE command lines (output folders and RIFE temp folders normalised) and ffprobes both
results. Real NVENC encodes, so it needs the GPU. Results go to tests/media/out/{py,cs}/<project>/.
"""
import json
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MEDIA = os.path.join(ROOT, "tests", "media")
PROJ = os.path.join(MEDIA, "projects")
LIB = os.path.abspath(os.path.join(ROOT, "..", "Dev", "library"))
PY = sys.executable
CLI = os.path.join(ROOT, "src", "ClipShrinker.Cli", "bin", "Debug", "net10.0", "ClipShrinker.Cli.exe")
RIFE_TMP = re.compile(r"[A-Za-z]:\\[^\"]*?clipshrinker_rife_[A-Za-z0-9_.]+", re.I)   # .NET temp names contain a dot


def norm(cmds, outdir):
    res = []
    for c in cmds:
        res.append([RIFE_TMP.sub("<RIFE_TMP>", x.replace(outdir, "<OUT>")) for x in c])
    return res


def ffprobe(p):
    r = subprocess.run([os.path.join(LIB, "ffprobe.exe"), "-v", "error", "-show_entries",
                        "stream=codec_type,codec_name,width,height,avg_frame_rate,color_transfer:format=duration,size",
                        "-of", "json", p], capture_output=True, text=True)
    return json.loads(r.stdout) if r.returncode == 0 else {"error": r.stderr.strip()}


def run(kind, name, extra):
    outdir = os.path.join(MEDIA, "out", kind, name)
    shutil.rmtree(outdir, ignore_errors=True)
    os.makedirs(outdir)
    log = os.path.join(outdir, "commands.json")
    proj = os.path.join(PROJ, name + ".clipproj")
    common = ["--out", outdir, "--presets", os.path.join(PROJ, "presets.json"), "--log-commands", log] + extra
    if kind == "py":
        cmd = [PY, "-I", os.path.join(HERE, "reference_export.py"), proj, "--tools", LIB] + common
    else:
        cmd = [CLI, "export", proj, "--tools", LIB] + common
    r = subprocess.run(cmd, capture_output=True, text=True)
    jobs = json.load(open(log, encoding="utf-8"))["jobs"] if os.path.exists(log) else []
    outs = sorted(f for f in os.listdir(outdir) if f.endswith(".mp4"))
    return {"code": r.returncode, "stdout": r.stdout, "stderr": r.stderr, "outdir": outdir,
            "jobs": [{"out": os.path.basename(j["out"]), "commands": norm(j["commands"], outdir)} for j in jobs],
            "files": {f: ffprobe(os.path.join(outdir, f)) for f in outs}}


def first_diff(a, b):
    for i, (x, y) in enumerate(zip(a, b)):
        if x != y:
            for k, (u, v) in enumerate(zip(x, y)):
                if u != v:
                    return f"command {i}, argument {k}:\n      python: {u[:300]}\n      c#:     {v[:300]}"
            return f"command {i}: length {len(x)} vs {len(y)}"
    return f"number of commands {len(a)} vs {len(b)}" if len(a) != len(b) else None


def main():
    names = sys.argv[1:] or sorted(os.path.splitext(f)[0] for f in os.listdir(PROJ) if f.endswith(".clipproj"))
    ok_all = True
    for name in names:
        py, cs = run("py", name, []), run("cs", name, [])
        problems = []
        if py["code"] != cs["code"]:
            problems.append(f"exit code python {py['code']} vs c# {cs['code']}")
        pj = [j["out"] for j in py["jobs"]]
        cj = [j["out"] for j in cs["jobs"]]
        if pj != cj:
            problems.append(f"output names {pj} vs {cj}")
        ncmd = 0
        for jp, jc in zip(py["jobs"], cs["jobs"]):
            ncmd += len(jp["commands"])
            d = first_diff(jp["commands"], jc["commands"])
            if d:
                problems.append(f"{jp['out']}: {d}")
        if sorted(py["files"]) != sorted(cs["files"]):
            problems.append(f"files {sorted(py['files'])} vs {sorted(cs['files'])}")
        for f in py["files"]:
            a, b = py["files"][f], cs["files"].get(f)
            if b is None:
                continue
            sa = [(s.get("codec_type"), s.get("codec_name"), s.get("width"), s.get("height"), s.get("color_transfer"))
                  for s in a.get("streams", [])]
            sb = [(s.get("codec_type"), s.get("codec_name"), s.get("width"), s.get("height"), s.get("color_transfer"))
                  for s in b.get("streams", [])]
            if sa != sb:
                problems.append(f"{f}: streams {sa} vs {sb}")
            da, db = float(a["format"]["duration"]), float(b["format"]["duration"])
            if abs(da - db) > 0.05:
                problems.append(f"{f}: duration {da} vs {db}")
        sizes = {f: (py["files"][f]["format"]["size"], cs["files"].get(f, {}).get("format", {}).get("size"))
                 for f in py["files"]}
        ok_all &= not problems
        print(f"{'OK  ' if not problems else 'DIFF'} {name}: {ncmd} commands, files {sizes}")
        for p in problems:
            print("     -", p)
        if problems or cs["code"] != 0:
            print("     c# stderr:", cs["stderr"].strip()[-800:])
            print("     py stderr:", py["stderr"].strip()[-800:])
    sys.exit(0 if ok_all else 1)


if __name__ == "__main__":
    main()
