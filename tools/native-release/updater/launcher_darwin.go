package main

import (
	"os/exec"
	"path/filepath"
)

func nativeLauncherCommand(release string) *exec.Cmd {
	return exec.Command(filepath.Join(release, "LauncherView.app", "Contents", "MacOS", "LauncherView"))
}
