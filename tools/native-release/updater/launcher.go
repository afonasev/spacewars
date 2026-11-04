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
	Title     string `json:"title"`
	Version   string `json:"version"`
	Message   string `json:"message"`
	Primary   string `json:"primary"`
	Secondary string `json:"secondary"`
	Progress  int    `json:"progress"`
	Busy      bool   `json:"busy"`
}

func launcherReleaseLabel(version, date string) string {
	if parsed, err := time.Parse("2006-01-02", date); err == nil {
		return version + " от " + parsed.Format("02.01.2006")
	}
	return version + " · дата не указана"
}

func launcherView(s Status, running bool) LauncherView {
	v := LauncherView{Title: "Игра готова к запуску", Version: "Текущая версия " + launcherReleaseLabel(s.Version, s.ReleaseDate), Primary: "Играть сейчас", Progress: -1}
	if channel == "test" {
		v.Version += " · test"
	}
	switch s.State {
	case "checking":
		v.Title = "Проверяем обновления"
		v.Message = "Проверка обновлений…"
	case "current":
		v.Message = "Последняя версия — проверено"
	case "offline":
		v.Title = "Проверка недоступна"
		v.Message = "Не удалось проверить обновления"
	case "available":
		v.Title = "Доступно обновление"
		v.Message = "Доступна новая версия " + launcherReleaseLabel(s.Available, s.AvailableDate)
		v.Secondary = "Обновить"
	case "downloading":
		v.Title = "Загружаем обновление"
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
		v.Title = "Обновление готово"
		v.Message = "Готова к установке версия " + launcherReleaseLabel(s.Available, s.AvailableDate)
		v.Secondary = "Установить"
	case "applying":
		v.Title = "Устанавливаем обновление"
		v.Message = "Установка…"
		v.Busy = true
	case "error":
		v.Title = "Обновление прервалось"
		v.Message = "Обновление не удалось"
		if s.Available != "" {
			v.Secondary = "Повторить"
		}
	}
	if s.Notice != "" {
		if v.Message != "" {
			v.Message = s.Notice + "\n" + v.Message
		} else {
			v.Message = s.Notice
		}
	}
	if running {
		v.Title = "Spacewars запущена"
		v.Primary = "Игра запущена"
		v.Busy = true
		v.Secondary = ""
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
