package main

import (
	"os/exec"
	"path/filepath"
	"syscall"
)

func nativeLauncherCommand(release string) *exec.Cmd {
	c := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-STA", "-ExecutionPolicy", "Bypass", "-File", filepath.Join(release, "LauncherView.ps1"))
	c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	return c
}
