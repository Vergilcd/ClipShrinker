@echo off
rem First-time setup (ffmpeg download / portable Python download). ClipShrinker.exe runs this automatically when needed.
rem "Setup.bat portable" forces the portable Python download even when Python is installed (to prepare a bundle).
cd /d "%~dp0"

rem ---- 1. ffmpeg (bundled; downloaded once if missing) ----
if exist ffmpeg.exe if exist ffprobe.exe goto have_ffmpeg
echo ffmpeg not found next to this file. Downloading it once (about 80 MB)...
powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $u='https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip'; $z=Join-Path $env:TEMP 'ffmpeg-clip.zip'; Invoke-WebRequest $u -OutFile $z; $d=Join-Path $env:TEMP 'ffmpeg-clip'; if(Test-Path $d){Remove-Item $d -Recurse -Force}; Expand-Archive $z $d; $b=(Get-ChildItem $d -Recurse -Filter ffmpeg.exe | Select-Object -First 1).DirectoryName; Copy-Item (Join-Path $b 'ffmpeg.exe') .; Copy-Item (Join-Path $b 'ffprobe.exe') .; Copy-Item (Join-Path $b '*.dll') .; Remove-Item $z,$d -Recurse -Force"
if errorlevel 1 (
  echo Could not download ffmpeg. Put ffmpeg.exe, ffprobe.exe and the ffmpeg DLL files in this folder manually.
  pause
  exit /b 1
)
:have_ffmpeg

rem ---- 2. Python: library\python (portable) -> Python installed on the PC -> download the portable one ----
if /i "%~1"=="portable" goto get_portable
if exist python\pythonw.exe goto check_done
set "PYW="
where pyw >nul 2>&1 && set "PYW=pyw -3"
if not defined PYW where pythonw >nul 2>&1 && set "PYW=pythonw"
if defined PYW goto run

:get_portable
echo Python was not found. Downloading portable Python 3.12 (about 40 MB, one time, nothing is installed)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0get-python.ps1"
if errorlevel 1 (
  echo.
  echo The portable Python could not be prepared. Details: library\portable-python-log.txt
  if /i "%~1"=="portable" (
    pause
    exit /b 1
  )
  goto winget
)
:check_done
if /i "%~1"=="portable" (
  echo.
  echo Portable Python is ready in the library\python folder. You can close this window.
  pause
  exit /b 0
)
:run_portable
start "" python\pythonw.exe video_shrinker.py
exit /b 0

:winget
echo The portable Python could not be downloaded. Installing Python 3.12 for your user account instead (one time)...
winget install -e --id Python.Python.3.12 --scope user --accept-package-agreements --accept-source-agreements
if errorlevel 1 (
  echo.
  echo Automatic install failed. Install Python from https://www.python.org/downloads/
  echo (tick "Add python.exe to PATH"), then double-click ClipShrinker.exe again.
  pause
  exit /b 1
)
echo.
echo Python installed. Close this window and double-click ClipShrinker.exe again.
pause
exit /b 0

:run
start "" %PYW% video_shrinker.py
