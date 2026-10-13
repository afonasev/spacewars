package main

import (
	"strings"
	"testing"
)

func TestWindowsApplyCommandQuotesStage(t *testing.T) {
	for _, stage := range []string{`C:\Users\Test User\AppData\Local\Spacewars\staged\abc`, `C:\Users\O'Brien\Обновления\abc`} {
		script := windowsApplyScript(`C:\Program Files\Spacewars\UpdateHost.exe`, stage)
		if !strings.Contains(script, "-FilePath 'C:\\Program Files\\Spacewars\\UpdateHost.exe'") || !strings.Contains(script, "-ArgumentList "+psQuote(`--apply "`+stage+`"`)) {
			t.Fatal("paths must remain separate quoted arguments:", script)
		}
		if strings.Contains(script, `\"`) {
			t.Fatal("literal quotes break Windows argument parsing:", script)
		}
		if !strings.HasPrefix(script, "$ErrorActionPreference='Stop';") || !strings.Contains(script, "-Verb RunAs -Wait -PassThru; exit $p.ExitCode") {
			t.Fatal("apply failure must propagate:", script)
		}
	}
}
