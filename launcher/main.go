package main

import (
	"os"
	"os/exec"
	"path/filepath"
	"syscall"
)

const createNoWindow = 0x08000000

func exists(p string) bool { _, err := os.Stat(p); return err == nil }

func main() {
	exe, err := os.Executable()
	if err != nil {
		return
	}
	dir := filepath.Join(filepath.Dir(exe), "library") // everything except the exe and the txt files lives here
	os.Chdir(dir)

	ready := exists("ffmpeg.exe") && exists("ffprobe.exe") && exists("video_shrinker.py")

	if ready {
		// 1. the portable Python that lives next to the program (library\python), if there is one
		if exists(filepath.Join("python", "pythonw.exe")) {
			c := exec.Command(filepath.Join(dir, "python", "pythonw.exe"), "video_shrinker.py")
			c.Dir = dir
			c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
			if c.Start() == nil {
				return
			}
		}
		// 2. a Python installed on the PC: py launcher first, then pythonw
		if p, err := exec.LookPath("pyw"); err == nil {
			c := exec.Command(p, "-3", "video_shrinker.py")
			c.Dir = dir
			c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
			if c.Start() == nil {
				return
			}
		}
		if p, err := exec.LookPath("pythonw"); err == nil {
			c := exec.Command(p, "video_shrinker.py")
			c.Dir = dir
			c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
			if c.Start() == nil {
				return
			}
		}
	}
	// something is missing (ffmpeg download / Python install): run the visible setup script
	c := exec.Command("cmd", "/c", "Setup.bat")
	c.Dir = dir
	c.Start()
}
