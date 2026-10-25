@echo off
title Building slim ffmpeg for Clip Shrinker
cd /d "%~dp0"
echo This builds a slim ffmpeg for Clip Shrinker. It takes 30 to 60 minutes.
echo.
if exist C:\msys64\msys2_shell.cmd goto have_msys
echo Installing MSYS2 - the free build tools - through winget...
winget install -e --id MSYS2.MSYS2 --accept-package-agreements --accept-source-agreements
:have_msys
if exist C:\msys64\msys2_shell.cmd goto build
echo.
echo MSYS2 is not installed. Install it from https://www.msys2.org and run this file again.
pause
exit /b 1
:build
echo.
echo Building - leave this window open.
call C:\msys64\msys2_shell.cmd -mingw64 -defterm -no-start -here -c "pacman -Sy --noconfirm; bash ./build-ffmpeg-min.sh 2>&1 | tee build-log.txt; echo; echo FINISHED - see build-log.txt"
echo.
if exist dist\ffmpeg.exe goto ok
echo The build did not finish. Run this file once more. If it fails again, send me build-log.txt.
pause
exit /b 1
:ok
echo Done - the new ffmpeg.exe and ffprobe.exe are in the dist folder.
pause
