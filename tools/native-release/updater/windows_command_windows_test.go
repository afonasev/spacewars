package main

import (
	"encoding/json"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"syscall"
	"testing"
	"time"
	"unsafe"
)

// The test binary acts as the helper to inspect real Windows argv.
func TestMain(m *testing.M) {
	if len(os.Args) > 1 && os.Args[1] == "--apply" && os.Getenv("SPACEWARS_ARGV_FIXTURE") != "" {
		b, _ := json.Marshal(os.Args[1:])
		if err := os.WriteFile(os.Getenv("SPACEWARS_ARGV_FIXTURE"), b, 0600); err != nil {
			os.Exit(1)
		}
		os.Exit(0)
	}
	os.Exit(m.Run())
}

func TestWindowsApplyScriptRoundTrip(t *testing.T) {
	output := filepath.Join(t.TempDir(), "argv.json")
	t.Setenv("SPACEWARS_ARGV_FIXTURE", output)
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	stage := `C:\Users\O'Brien Test\AppData\Local\Обновления\staged\abc`
	// Suppress elevation only; use real Windows command-line joining/parsing.
	shim := `function Start-Process { param($FilePath,$ArgumentList,$Verb,[switch]$Wait,[switch]$PassThru) Microsoft.PowerShell.Management\Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait:$Wait -PassThru:$PassThru }; `
	if b, err := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", shim+windowsApplyScript(exe, stage)).CombinedOutput(); err != nil {
		t.Fatalf("PowerShell: %s: %v", b, err)
	}
	b, err := os.ReadFile(output)
	if err != nil {
		t.Fatal(err)
	}
	var args []string
	if err := json.Unmarshal(b, &args); err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(args, []string{"--apply", stage}) {
		t.Fatalf("argv split/corrupted: %#v", args)
	}
}

// Run on an interactive Windows desktop: this catches a hidden WinForms window,
// script startup/encoding errors and broken redirected stdin together.
func TestWindowsLauncherVisibleWithRedirectedPipes(t *testing.T) {
	release := t.TempDir()
	for dest, source := range map[string]string{"LauncherView.ps1": "../launcher/LauncherView.ps1", "Spacewars.ico": "../icons/Spacewars.ico"} {
		b, err := os.ReadFile(source)
		if err != nil {
			t.Fatal(err)
		}
		if err = os.WriteFile(filepath.Join(release, dest), b, 0600); err != nil {
			t.Fatal(err)
		}
	}
	cmd := nativeLauncherCommand(release)
	if cmd.SysProcAttr.HideWindow || cmd.SysProcAttr.CreationFlags&0x08000000 == 0 {
		t.Fatal("must hide console without hiding native window")
	}
	l, err := startLauncherCommand(cmd)
	if err != nil {
		t.Fatal(err)
	}
	defer l.close()
	if err = l.send(launcherView(Status{Version: "1.0.0", State: "downloading", Done: 6, Total: 10}, false)); err != nil {
		t.Fatal(err)
	}
	user := syscall.NewLazyDLL("user32.dll")
	enum := user.NewProc("EnumWindows")
	pid := user.NewProc("GetWindowThreadProcessId")
	visible := user.NewProc("IsWindowVisible")
	found := false
	callback := syscall.NewCallback(func(h, unused uintptr) uintptr {
		var id uint32
		pid.Call(h, uintptr(unsafe.Pointer(&id)))
		if id == uint32(cmd.Process.Pid) {
			if v, _, _ := visible.Call(h); v != 0 {
				found = true
			}
		}
		return 1
	})
	deadline := time.After(20 * time.Second)
	tick := time.NewTicker(100 * time.Millisecond)
	defer tick.Stop()
	for {
		select {
		case <-l.closed:
			t.Fatalf("launcher exited before showing UI: %v", l.err)
		case <-deadline:
			t.Fatal("no visible launcher on Windows desktop")
		case <-tick.C:
			enum.Call(callback, 0)
			if found {
				return
			}
		}
	}
}
