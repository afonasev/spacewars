//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"unsafe"
)

func replaceFile(a, b string) error {
	aa, e := syscall.UTF16PtrFromString(a)
	if e != nil {
		return e
	}
	bb, e := syscall.UTF16PtrFromString(b)
	if e != nil {
		return e
	}
	r, _, err := syscall.NewLazyDLL("kernel32.dll").NewProc("MoveFileExW").Call(uintptr(unsafe.Pointer(aa)), uintptr(unsafe.Pointer(bb)), 3)
	if r == 0 {
		return err
	}
	return nil
}
func psQuote(s string) string { return "'" + strings.ReplaceAll(s, "'", "''") + "'" }
func applyWithPermission(exe, stage string) error {
	// Fixed protected executable and verb. The elevated helper verifies all staged bytes.
	script := "$p = Start-Process -FilePath " + psQuote(exe) + " -ArgumentList @('--apply', '\"'+" + psQuote(stage) + "+'\"') -Verb RunAs -Wait -PassThru; exit $p.ExitCode"
	b, e := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script).CombinedOutput()
	if e != nil {
		return fmt.Errorf("update authorization/apply failed: %s: %w", b, e)
	}
	return nil
}
func rollbackWithPermission(exe string) error {
	script := "$p=Start-Process -FilePath " + psQuote(exe) + " -ArgumentList '--rollback' -Verb RunAs -Wait -PassThru; exit $p.ExitCode"
	return exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script).Run()
}

func acquireApplyLock(root string) (func(), error) {
	f, e := os.OpenFile(filepath.Join(root, "apply.lock"), os.O_CREATE|os.O_RDWR, 0600)
	if e != nil {
		return nil, e
	}
	overlap := new(syscall.Overlapped)
	r, _, err := syscall.NewLazyDLL("kernel32.dll").NewProc("LockFileEx").Call(f.Fd(), 3, 0, 1, 0, uintptr(unsafe.Pointer(overlap)))
	if r == 0 {
		f.Close()
		return nil, err
	}
	return func() { f.Close() }, nil
}
