package main

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sync"
	"time"
)

func platformID() string {
	if runtime.GOOS == "windows" {
		return "windows-x64"
	}
	return "macos-universal"
}
func executableName(s string) string {
	if runtime.GOOS == "windows" {
		return s + ".exe"
	}
	return s
}
func hostRoot(exe string) (string, error) {
	p := filepath.Dir(exe)
	if !hashOK(filepath.Base(p)) || filepath.Base(filepath.Dir(p)) != "releases" {
		return "", errors.New("helper must run from installed release")
	}
	return filepath.Dir(filepath.Dir(p)), nil
}

var restartRequested = errors.New("restart requested")
var crashAfterReady = errors.New("game failed after readiness")

func main() {
	if e := run(); e != nil {
		if errors.Is(e, restartRequested) {
			os.Exit(11)
		}
		if errors.Is(e, crashAfterReady) {
			os.Exit(12)
		}
		fmt.Fprintln(os.Stderr, e)
		os.Exit(1)
	}
}
func run() error {
	if len(os.Args) > 1 && os.Args[1] == "--pack" {
		return pack(os.Args[2:])
	}
	if len(os.Args) == 3 && os.Args[1] == "--keygen" {
		return keygen(os.Args[2])
	}
	exe, e := os.Executable()
	if e != nil {
		return e
	}
	exe, e = filepath.EvalSymlinks(exe)
	if e != nil {
		return e
	}
	if len(os.Args) == 3 && os.Args[1] == "--activate" {
		return activateInstalled(filepath.Dir(exe), os.Args[2], platformID())
	}
	if len(os.Args) > 1 && (os.Args[1] == "--apply" || os.Args[1] == "--rollback") {
		root, e := hostRoot(exe)
		if e != nil {
			return e
		}
		if os.Args[1] == "--rollback" {
			unlock, e := acquireApplyLock(root)
			if e != nil {
				return e
			}
			defer unlock()
			a, e := readActive(root)
			if e != nil {
				return e
			}
			if !hashOK(a.Previous) {
				return errors.New("no recovery release")
			}
			if _, e = installed(root, a.Previous, platformID()); e != nil {
				return e
			}
			return atomicJSON(filepath.Join(root, "active.json"), Active{ID: a.Previous})
		}
		if len(os.Args) != 3 {
			return errors.New("missing stage")
		}
		return applyStage(root, os.Args[2], platformID())
	}
	if len(os.Args) > 1 && os.Args[1] == "--host" {
		root, e := hostRoot(exe)
		if e != nil {
			return e
		}
		return host(root, exe)
	}
	root := filepath.Dir(exe)
	if runtime.GOOS == "darwin" {
		seed := filepath.Join(filepath.Dir(root), "Resources")
		base, e := os.UserConfigDir()
		if e != nil {
			return e
		}
		root = filepath.Join(base, "Spacewars", "native-v1")
		if e = seedMacStore(seed, root); e != nil {
			return e
		}
	}
	for {
		a, e := readActive(root)
		if e != nil {
			return e
		}
		m, verifyError := installed(root, a.ID, platformID())
		if verifyError == nil {
			verifyError = verifyReleaseFiles(root, m)
		}
		if verifyError != nil {
			if e = recoverPrevious(root, a); e != nil {
				return verifyError
			}
			continue
		}
		c := exec.Command(filepath.Join(root, "releases", a.ID, executableName("UpdateHost")), "--host")
		c.Stdout = os.Stdout
		c.Stderr = os.Stderr
		e = c.Run()
		var exited *exec.ExitError
		if errors.As(e, &exited) && exited.ExitCode() == 11 {
			continue
		}
		if errors.As(e, &exited) && exited.ExitCode() == 12 {
			return nil
		}
		if e != nil && hashOK(a.Previous) {
			if recoverPrevious(root, a) == nil {
				continue
			}
		}
		return e
	}
}
func recoverPrevious(root string, a Active) error {
	if !hashOK(a.Previous) {
		return errors.New("no previous signed release")
	}
	m, e := installed(root, a.Previous, platformID())
	if e != nil {
		return e
	}
	if e = verifyReleaseFiles(root, m); e != nil {
		return e
	}
	if base, e := os.UserCacheDir(); e == nil {
		dir := filepath.Join(base, "Spacewars", "native-v1")
		_ = os.MkdirAll(dir, 0700)
		_ = os.WriteFile(filepath.Join(dir, "last-error.txt"), []byte("candidate bootstrap failed"), 0600)
	}
	return rollbackWithPermission(filepath.Join(root, "releases", a.Previous, executableName("UpdateHost")))
}

type Status struct {
	Notice    string `json:"notice"`
	Version   string `json:"version"`
	Available string `json:"available"`
	State     string `json:"state"`
	Error     string `json:"error"`
	Done      int64  `json:"done"`
	Total     int64  `json:"total"`
}

func host(root, exe string) error {
	platform := platformID()
	a, e := readActive(root)
	if e != nil {
		return e
	}
	current, e := installed(root, a.ID, platform)
	if e != nil {
		return e
	}
	cacheBase, e := os.UserCacheDir()
	if e != nil {
		return e
	}
	cache := filepath.Join(cacheBase, "Spacewars", "native-v1")
	tokenBytes := make([]byte, 32)
	if _, e = rand.Read(tokenBytes); e != nil {
		return e
	}
	token := hex.EncodeToString(tokenBytes)
	listener, e := net.Listen("tcp", "127.0.0.1:0")
	if e != nil {
		return e
	}
	var mu sync.Mutex
	status := Status{Version: current.Version, State: "checking"}
	if _, e := os.Stat(filepath.Join(cache, "last-error.txt")); e == nil {
		status.Notice = "Предыдущее обновление не удалось. Рабочая версия сохранена."
		_ = os.Remove(filepath.Join(cache, "last-error.txt"))
	}
	var next Manifest
	var raw []byte
	var stage string
	commit := false
	ready := false
	menu := false
	mux := http.NewServeMux()
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Authorization") != "Bearer "+token {
			http.Error(w, "unauthorized", 401)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		w.Header().Set("Cache-Control", "no-store")
		mu.Lock()
		defer mu.Unlock()
		switch r.URL.Path {
		case "/status":
			if r.Method != "GET" {
				http.Error(w, "method", 405)
				return
			}
		case "/ready":
			if r.Method != "POST" {
				http.Error(w, "method", 405)
				return
			}
			ready = true
			menu = true
		case "/play":
			if r.Method != "POST" || status.State == "downloading" || status.State == "prepared" {
				http.Error(w, "busy", 409)
				return
			}
			menu = false
		case "/menu":
			if r.Method != "POST" {
				http.Error(w, "method", 405)
				return
			}
			menu = true
		case "/update":
			if r.Method != "POST" || !menu || status.State != "available" && status.State != "error" || next.Sequence <= current.Sequence {
				http.Error(w, "unavailable", 409)
				return
			}
			status.State = "downloading"
			status.Error = ""
			m, b := next, append([]byte(nil), raw...)
			go func() {
				s, e := stageUpdate(root, cache, m, b, func(d, t int64) { mu.Lock(); status.Done = d; status.Total = t; mu.Unlock() })
				mu.Lock()
				defer mu.Unlock()
				if e != nil {
					status.State = "error"
					status.Error = e.Error()
				} else {
					stage = s
					status.State = "prepared"
				}
			}()
		case "/commit":
			if r.Method != "POST" || !menu || status.State != "prepared" {
				http.Error(w, "not prepared", 409)
				return
			}
			commit = true
			status.State = "applying"
		default:
			http.NotFound(w, r)
			return
		}
		_ = json.NewEncoder(w).Encode(status)
	})
	server := &http.Server{Handler: mux, ReadHeaderTimeout: 5 * time.Second}
	go server.Serve(listener)
	defer server.Close()
	go func() {
		b, m, e := candidate(platform)
		mu.Lock()
		defer mu.Unlock()
		if e != nil {
			status.State = "offline"
			status.Error = e.Error()
			return
		}
		if m.Sequence > current.Sequence {
			next = m
			raw = b
			status.Available = m.Version
			status.State = "available"
		} else {
			status.State = "current"
		}
	}()
	game := exec.Command(filepath.Join(root, "releases", a.ID, filepath.FromSlash(current.Entry)))
	game.Env = append(os.Environ(), "SPACEWARS_UPDATE_ENDPOINT=http://"+listener.Addr().String(), "SPACEWARS_UPDATE_TOKEN="+token)
	game.Stdout = os.Stdout
	game.Stderr = os.Stderr
	if e = game.Start(); e != nil {
		return e
	}
	exit := make(chan error, 1)
	go func() { exit <- game.Wait() }()
	timer := time.NewTimer(90 * time.Second)
	defer timer.Stop()
	var gameErr error
	select {
	case gameErr = <-exit:
	case <-timer.C:
		mu.Lock()
		ok := ready
		mu.Unlock()
		if !ok {
			_ = game.Process.Kill()
		}
		gameErr = <-exit
	}
	mu.Lock()
	doCommit, healthy := commit, ready
	mu.Unlock()
	if doCommit {
		if e = applyWithPermission(exe, stage); e != nil {
			_ = os.WriteFile(filepath.Join(cache, "last-error.txt"), []byte(e.Error()), 0600)
		}
		return restartBootstrap(root)
	}
	if gameErr != nil && !healthy && hashOK(a.Previous) {
		if e = rollbackWithPermission(exe); e != nil {
			return fmt.Errorf("bootstrap recovery failed: %w", e)
		}
		return restartBootstrap(root)
	}
	return classifyPlayerExit(healthy, gameErr)
}
func classifyPlayerExit(ready bool, err error) error {
	if ready && err != nil {
		return crashAfterReady
	}
	return err
}
func restartBootstrap(root string) error { return restartRequested }
