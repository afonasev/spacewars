//go:build !darwin && !windows

package main

import "os/exec"

func nativeLauncherCommand(release string) *exec.Cmd { return exec.Command("false") }
