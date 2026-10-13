package main

import "strings"

func psQuote(s string) string { return "'" + strings.ReplaceAll(s, "'", "''") + "'" }

func windowsApplyScript(exe, stage string) string {
	// Start-Process joins ArgumentList into one Windows command line. Use real
	// quotes around the stage argument; backslash-quote passes literal quotes.
	return "$ErrorActionPreference='Stop'; $p = Start-Process -FilePath " + psQuote(exe) + " -ArgumentList " + psQuote("--apply \""+stage+"\"") + " -Verb RunAs -Wait -PassThru; exit $p.ExitCode"
}
