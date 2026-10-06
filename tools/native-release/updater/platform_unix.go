//go:build !windows

package main

import (
	"os"
	"os/exec"
	"path/filepath"
	"syscall"
)

func replaceFile(a, b string) error               { return os.Rename(a, b) }
func applyWithPermission(exe, stage string) error { return exec.Command(exe, "--apply", stage).Run() }
func rollbackWithPermission(exe string) error     { return exec.Command(exe, "--rollback").Run() }

func acquireApplyLock(root string) (func(), error) {
	f, e := os.OpenFile(filepath.Join(root, "apply.lock"), os.O_CREATE|os.O_RDWR, 0600)
	if e != nil {
		return nil, e
	}
	if e = syscall.Flock(int(f.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); e != nil {
		f.Close()
		return nil, e
	}
	return func() { f.Close() }, nil
}
