package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io"
	"os/exec"
	"path/filepath"
	"sync"
	"time"
)

// The native view receives presentation only, never the IPC token or update paths.
type LauncherView struct {
	Version   string `json:"version"`
	Message   string `json:"message"`
	Primary   string `json:"primary"`
	Secondary string `json:"secondary"`
	Progress  int    `json:"progress"`
	Busy      bool   `json:"busy"`
}

func launcherView(s Status, running bool) LauncherView {
	v := LauncherView{Version: s.Version, Primary: "Играть", Progress: -1}
	if channel == "test" {
		v.Version += " · test"
	}
	if running {
		v.Primary = "Игра запущена"
		v.Busy = true
		return v
	}
	switch s.State {
	case "checking":
		v.Message = "Проверка обновлений…"
	case "available":
		v.Secondary = "Обновить до " + s.Available
	case "downloading":
		v.Message = "Загрузка…"
		v.Secondary = "Отменить"
		if s.Total > 0 {
			v.Progress = int(float64(s.Done) / float64(s.Total) * 100)
			if v.Progress > 100 {
				v.Progress = 100
			}
			if v.Progress < 0 {
				v.Progress = 0
			}
			v.Message = fmt.Sprintf("Загрузка · %d%%", v.Progress)
		}
	case "prepared":
		v.Secondary = "Установить " + s.Available
	case "applying":
		v.Message = "Установка…"
		v.Busy = true
	case "error":
		v.Message = "Обновление не удалось"
		if s.Available != "" {
			v.Secondary = "Повторить"
		}
	}
	if v.Message == "" && s.Notice != "" {
		v.Message = s.Notice
	}
	return v
}

type desktopLauncher struct {
	actions chan string
	closed  chan struct{}
	input   io.WriteCloser
	cmd     *exec.Cmd
	err     error
	mu      sync.Mutex
}

var launcherForHost = startDesktopLauncher

func startDesktopLauncher(release string) (*desktopLauncher, error) {
	return startLauncherCommand(nativeLauncherCommand(release))
}
func startLauncherCommand(cmd *exec.Cmd) (*desktopLauncher, error) {
	input, err := cmd.StdinPipe()
	if err != nil {
		return nil, err
	}
	output, err := cmd.StdoutPipe()
	if err != nil {
		input.Close()
		return nil, err
	}
	if err = cmd.Start(); err != nil {
		input.Close()
		return nil, err
	}
	l := &desktopLauncher{actions: make(chan string, 8), closed: make(chan struct{}), input: input, cmd: cmd}
	go func() {
		scanner := bufio.NewScanner(output)
		scanner.Buffer(make([]byte, 1024), 4096)
		for scanner.Scan() {
			action := scanner.Text()
			switch action {
			case "play", "secondary":
				select {
				case l.actions <- action:
				default:
				}
			}
		}
		l.err = cmd.Wait()
		if l.err == nil {
			l.err = scanner.Err()
		}
		close(l.closed)
	}()
	return l, nil
}
func (l *desktopLauncher) send(v LauncherView) error {
	l.mu.Lock()
	defer l.mu.Unlock()
	return json.NewEncoder(l.input).Encode(v)
}
func (l *desktopLauncher) close() {
	l.input.Close()
	select {
	case <-l.closed:
	case <-time.After(2 * time.Second):
		l.cmd.Process.Kill()
		<-l.closed
	}
}
func launcherRelease(root, id string) string { return filepath.Join(root, "releases", id) }
