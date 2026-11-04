package main

import (
	"os/exec"
	"path/filepath"
	"syscall"
)

func nativeLauncherCommand(release string) *exec.Cmd {
	c := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-STA", "-ExecutionPolicy", "Bypass", "-File", filepath.Join(release, "LauncherView.ps1"))
	// Hide only the console. HideWindow sets STARTF_USESHOWWINDOW/SW_HIDE,
	// which can suppress the first WinForms window as well.
	c.SysProcAttr = &syscall.SysProcAttr{CreationFlags: 0x08000000} // CREATE_NO_WINDOW
	return c
}
