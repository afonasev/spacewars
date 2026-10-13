package main

import (
	"encoding/json"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"testing"
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
