@echo off
rem Downloads the portable Python into library\python (needed once, to make a bundle that works without any download).
cd /d "%~dp0..\library"
call Setup.bat portable
