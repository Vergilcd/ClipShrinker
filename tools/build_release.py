#!/usr/bin/env python3
"""Assemble ClipShrinker_v<version>.zip from this repo plus a folder of heavy binaries.

Usage: python tools/build_release.py --assets ASSETS_DIR [--out dist]
ASSETS_DIR must contain: ffmpeg.exe, ffprobe.exe, rife/, python/, ClipShrinker.exe
"""
import argparse, re, shutil, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ROOT_FILES = ["README.txt", "CHANGELOG.txt", "LICENSE.txt", "ffmpeg-LICENSE.txt", "ffmpeg-build-settings.txt"]
LIB_FILES = ["video_shrinker.py", "clipshrinker.ico", "Setup.bat", "get-python.ps1"]


def version():
    m = re.search(r'APP_NAME,\s*APP_VERSION\s*=\s*"[^"]*",\s*"([^"]+)"', (ROOT / "library/video_shrinker.py").read_text(encoding="utf-8"))
    return m.group(1) if m else sys.exit("APP_VERSION not found")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--assets", required=True, type=Path)
    ap.add_argument("--out", default=ROOT / "dist", type=Path)
    a = ap.parse_args()
    need = ["ffmpeg.exe", "ffprobe.exe", "rife", "python", "ClipShrinker.exe"]
    missing = [n for n in need if not (a.assets / n).exists()]
    if missing:
        sys.exit("assets missing: " + ", ".join(missing))
    v = version()
    stage = a.out / "stage" / "ClipShrinker"
    shutil.rmtree(a.out / "stage", ignore_errors=True)
    (stage / "library").mkdir(parents=True)
    for f in ROOT_FILES:
        shutil.copy2(ROOT / f, stage / f)
    for f in LIB_FILES:
        shutil.copy2(ROOT / "library" / f, stage / "library" / f)
    shutil.copytree(ROOT / "library/sv_ttk", stage / "library/sv_ttk", ignore=shutil.ignore_patterns("__pycache__"))
    shutil.copy2(a.assets / "ClipShrinker.exe", stage / "ClipShrinker.exe")
    for f in ("ffmpeg.exe", "ffprobe.exe"):
        shutil.copy2(a.assets / f, stage / "library" / f)
    shutil.copytree(a.assets / "rife", stage / "library/rife")
    shutil.copytree(a.assets / "python", stage / "library/python")
    zp = a.out / f"ClipShrinker_v{v}.zip"
    with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(stage.parent.rglob("*")):
            if p.is_file():
                z.write(p, p.relative_to(stage.parent))
    shutil.rmtree(a.out / "stage")
    print(zp, zp.stat().st_size)


if __name__ == "__main__":
    main()
