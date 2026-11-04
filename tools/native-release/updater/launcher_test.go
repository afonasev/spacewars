package main

import (
	"context"
	"crypto/ed25519"
	"crypto/rand"
	"encoding/base64"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"testing"
	"time"
)

func TestLauncherMinimalStates(t *testing.T) {
	for _, s := range []Status{{State: "current", Version: "1"}, {State: "offline", Version: "1", Error: "network"}, {State: "checking", Version: "1"}} {
		v := launcherView(s, false)
		if v.Primary != "Играть сейчас" || v.Secondary != "" || v.Busy {
			t.Fatalf("installed game must remain playable: %+v", v)
		}
	}
	v := launcherView(Status{State: "downloading", Total: 10, Done: 6}, false)
	if v.Progress != 60 || v.Secondary != "Отменить" {
		t.Fatal(v)
	}
	v = launcherView(Status{State: "prepared", Available: "2"}, false)
	if v.Secondary != "Установить" {
		t.Fatal(v)
	}
	v = launcherView(Status{State: "available", Available: "2"}, true)
	if !v.Busy || v.Secondary != "" || v.Message != "Доступна новая версия 2 · дата не указана" {
		t.Fatal(v)
	}
}
func TestLauncherVersionDatesAndDiscoveryTruth(t *testing.T) {
	for _, tt := range []struct{ state, message, title string }{
		{"checking", "Проверка обновлений…", "Проверяем обновления"},
		{"offline", "Не удалось проверить обновления", "Проверка недоступна"},
		{"current", "Последняя версия — проверено", "Игра готова к запуску"},
		{"available", "Доступна новая версия 1.0.1 от 04.11.2026", "Доступно обновление"},
		{"prepared", "Готова к установке версия 1.0.1 от 04.11.2026", "Обновление готово"},
	} {
		for _, running := range []bool{false, true} {
			v := launcherView(Status{State: tt.state, Version: "1.0.0", ReleaseDate: "2026-11-03", Available: "1.0.1", AvailableDate: "2026-11-04"}, running)
			wantVersion := "Текущая версия 1.0.0 от 03.11.2026"
			if channel == "test" {
				wantVersion += " · test"
			}
			if v.Version != wantVersion || v.Message != tt.message {
				t.Fatalf("%s running=%v: %+v", tt.state, running, v)
			}
			wantTitle := tt.title
			if running {
				wantTitle = "Spacewars запущена"
			}
			if v.Title != wantTitle {
				t.Fatalf("%s: title %q", tt.state, v.Title)
			}
			if running && (!v.Busy || v.Secondary != "") {
				t.Fatal(v)
			}
		}
	}
	for _, date := range []string{"", "not-a-date", "2026-02-30"} {
		if got := launcherReleaseLabel("1", date); got != "1 · дата не указана" {
			t.Fatal(got)
		}
	}
}
func TestLauncherHostPersistentAndCloseDuringPlayer(t *testing.T) {
	for _, closePlaying := range []bool{false, true} {
		t.Run(map[bool]string{false: "return-and-replay", true: "close-during-player"}[closePlaying], func(t *testing.T) {
			oldLauncher, oldDiscover, oldChannel, oldKey := launcherForHost, discoverForHost, channel, publicKey
			defer func() {
				launcherForHost = oldLauncher
				discoverForHost = oldDiscover
				channel = oldChannel
				publicKey = oldKey
			}()
			channel = "test"
			root := t.TempDir()
			t.Setenv("SPACEWARS_LAUNCHER_FIXTURE", root)
			base, _ := os.UserCacheDir()
			defer os.RemoveAll(filepath.Join(base, "Spacewars", storeNamespace(), digest([]byte(root))))
			pub, key, _ := ed25519.GenerateKey(rand.Reader)
			publicKey = base64.StdEncoding.EncodeToString(pub)
			script := []byte(`#!/usr/bin/env python3
import os,pathlib,urllib.request,time
p=pathlib.Path(os.environ['SPACEWARS_LAUNCHER_FIXTURE'])
count=p/'count'; n=int(count.read_text())+1 if count.exists() else 1;count.write_text(str(n))
r=urllib.request.Request(os.environ['SPACEWARS_UPDATE_ENDPOINT']+'/ready',method='POST',headers={'Authorization':'Bearer '+os.environ['SPACEWARS_UPDATE_TOKEN']})
urllib.request.urlopen(r).read()
for i in range(500):
 if (p/('exit-'+str(n))).exists():break
 time.sleep(.01)
else:raise RuntimeError('fixture timeout')
(p/('exited-'+str(n))).write_text('normal')
`)
			m := sample()
			m.Platform = platformID()
			m.Files[0].Size = int64(len(script))
			m.Files[0].Hash = digest(script)
			m.Files[0].Chunks = []Chunk{{Hash: digest(script), Size: int64(len(script))}}
			release := launcherRelease(root, m.ID)
			os.MkdirAll(release, 0755)
			os.WriteFile(filepath.Join(release, m.Entry), script, 0755)
			os.WriteFile(filepath.Join(release, "manifest.json"), signWith(m, key), 0644)
			atomicJSON(filepath.Join(root, "active.json"), Active{ID: m.ID})
			discoverForHost = func(string) ([]byte, Manifest, error) { return nil, Manifest{}, errors.New("offline fixture") }
			driver := `import sys,json,pathlib,os
p=pathlib.Path(os.environ['SPACEWARS_LAUNCHER_FIXTURE']); phase=0; frames=0
for line in sys.stdin:
 s=json.loads(line); frames+=1
 if phase==0 and frames>=2:
  assert not (p/'count').exists(), 'Player started without Play'
  print('play',flush=True);phase=1
 elif phase==1 and s['busy']:
  print('play',flush=True);print('secondary',flush=True)
  (p/'exit-1').write_text('exit');phase=2
 elif phase==2 and not s['busy']:
  assert (p/'exited-1').exists()
  assert (p/'count').read_text()=='1', 'duplicate Player'
  print('play',flush=True);phase=3
 elif phase==3 and s['busy']:
  (p/'exit-2').write_text('exit');phase=4
 elif phase==4 and not s['busy']:
  assert (p/'exited-2').exists();(p/'view-ok').write_text('ok');break
`
			if closePlaying {
				driver = `import sys,json,pathlib,os
p=pathlib.Path(os.environ['SPACEWARS_LAUNCHER_FIXTURE']);phase=0
for line in sys.stdin:
 s=json.loads(line)
 if phase==0: print('play',flush=True);phase=1
 elif s['busy'] and (p/'count').exists():
  (p/'view-ok').write_text('closed while running');break
`
			}
			launcherForHost = func(string) (*desktopLauncher, error) {
				return startLauncherCommand(exec.Command("python3", "-u", "-c", driver))
			}
			done := make(chan error, 1)
			go func() { done <- host(root, filepath.Join(release, "UpdateHost")) }()
			if closePlaying {
				deadline := time.After(5 * time.Second)
				for {
					if _, err := os.Stat(filepath.Join(root, "view-ok")); err == nil {
						break
					}
					select {
					case err := <-done:
						t.Fatalf("host exited during Player: %v", err)
					case <-deadline:
						t.Fatal("view fixture timed out")
					default:
						time.Sleep(10 * time.Millisecond)
					}
				}
				select {
				case err := <-done:
					t.Fatalf("closing launcher terminated Player: %v", err)
				case <-time.After(100 * time.Millisecond):
				}
				os.WriteFile(filepath.Join(root, "exit-1"), []byte("exit"), 0600)
			}
			select {
			case err := <-done:
				if err != nil {
					t.Fatal(err)
				}
			case <-time.After(8 * time.Second):
				t.Fatal("host timeout")
			}
			if _, err := os.Stat(filepath.Join(root, "view-ok")); err != nil {
				t.Fatal("view assertions failed", err)
			}
			if closePlaying {
				if _, err := os.Stat(filepath.Join(root, "exited-1")); err != nil {
					t.Fatal("Player did not exit normally")
				}
			}
		})
	}
}

func TestLauncherInstallBeforePlayer(t *testing.T) {
	oldLauncher, oldDiscover, oldStage, oldApply, oldChannel, oldKey := launcherForHost, discoverForHost, stageForHost, applyForHost, channel, publicKey
	defer func() {
		launcherForHost = oldLauncher
		discoverForHost = oldDiscover
		stageForHost = oldStage
		applyForHost = oldApply
		channel = oldChannel
		publicKey = oldKey
	}()
	channel = "test"
	root := t.TempDir()
	t.Setenv("SPACEWARS_LAUNCHER_FIXTURE", root)
	base, _ := os.UserCacheDir()
	defer os.RemoveAll(filepath.Join(base, "Spacewars", storeNamespace(), digest([]byte(root))))
	pub, key, _ := ed25519.GenerateKey(rand.Reader)
	publicKey = base64.StdEncoding.EncodeToString(pub)
	current := sample()
	current.Platform = platformID()
	release := launcherRelease(root, current.ID)
	os.MkdirAll(release, 0755)
	os.WriteFile(filepath.Join(release, "manifest.json"), signWith(current, key), 0644)
	atomicJSON(filepath.Join(root, "active.json"), Active{ID: current.ID})
	next := transportSample()
	next.Platform = platformID()
	next.Sequence = current.Sequence + 1
	next.Version = "1.0.1"
	raw := signWith(next, key)
	discoverForHost = func(string) ([]byte, Manifest, error) { return raw, next, nil }
	stageForHost = func(ctx context.Context, root, cache string, m Manifest, b []byte, progress func(int64, int64)) (string, error) {
		progress(6, 10)
		stage := filepath.Join(cache, "stage")
		os.MkdirAll(stage, 0700)
		os.WriteFile(filepath.Join(stage, "manifest.json"), b, 0600)
		return stage, nil
	}
	applied := 0
	applyForHost = func(exe, stage string) error {
		applied++
		if applied == 1 {
			return errors.New("denied fixture")
		}
		return nil
	}
	driver := `import sys,json,pathlib,os
p=pathlib.Path(os.environ['SPACEWARS_LAUNCHER_FIXTURE']);phase=0
for line in sys.stdin:
 s=json.loads(line)
 assert not s['busy'] or s['message']=='Установка…'
 if phase==0 and s['secondary'].startswith('Обновить'):
  print('secondary',flush=True);phase=1
 elif phase==1 and s['secondary'].startswith('Установить'):
  print('secondary',flush=True);phase=2
 elif phase==2 and s['message'].startswith('Не удалось установить'):
  (p/'retry-verified').write_text('old version remains playable')
  assert s['primary']=='Играть сейчас' and not s['busy']
  print('secondary',flush=True);phase=3
`
	launcherForHost = func(string) (*desktopLauncher, error) {
		return startLauncherCommand(exec.Command("python3", "-u", "-c", driver))
	}
	if err := host(root, filepath.Join(release, "UpdateHost")); !errors.Is(err, restartRequested) {
		t.Fatalf("expected bootstrap restart after install: %v", err)
	}
	if applied != 2 {
		t.Fatalf("apply/retry count: %d", applied)
	}
	if _, err := os.Stat(filepath.Join(root, "retry-verified")); err != nil {
		t.Fatal(err)
	}
	a, err := readActive(root)
	if err != nil || a.ID != current.ID {
		t.Fatal("host changed pointer outside privileged apply", err)
	}
}

// A durable staged update must never apply before the native window asks.
func TestLauncherPendingRequiresExplicitInstall(t *testing.T) {
	oldLauncher, oldApply, oldChannel, oldKey := launcherForHost, applyForHost, channel, publicKey
	defer func() {
		launcherForHost = oldLauncher
		applyForHost = oldApply
		channel = oldChannel
		publicKey = oldKey
	}()
	channel = "test"
	root := t.TempDir()
	t.Setenv("SPACEWARS_LAUNCHER_FIXTURE", root)
	base, _ := os.UserCacheDir()
	cache := filepath.Join(base, "Spacewars", storeNamespace(), digest([]byte(root)))
	defer os.RemoveAll(cache)
	pub, key, _ := ed25519.GenerateKey(rand.Reader)
	publicKey = base64.StdEncoding.EncodeToString(pub)
	current := sample()
	current.Platform = platformID()
	release := launcherRelease(root, current.ID)
	os.MkdirAll(release, 0755)
	os.WriteFile(filepath.Join(release, "manifest.json"), signWith(current, key), 0644)
	atomicJSON(filepath.Join(root, "active.json"), Active{ID: current.ID})
	next := transportSample()
	next.Platform = platformID()
	next.Sequence = current.Sequence + 1
	next.Version = "1.0.1"
	next.ID = digest([]byte("pending-launcher-update"))
	next.Transport.Tag = "v1.0.1"
	next.Transport.Asset = "Spacewars-1.0.1-" + next.Platform + ".pack"
	raw := signWith(next, key)
	stage := filepath.Join(cache, "staged", next.ID)
	os.MkdirAll(stage, 0700)
	os.WriteFile(filepath.Join(stage, "manifest.json"), raw, 0600)
	if err := writePending(root, cache, stage, next, raw); err != nil {
		t.Fatal(err)
	}
	if _, err := readPending(root, cache); err != nil {
		t.Fatal("invalid pending fixture", err)
	}
	calls := 0
	applyForHost = func(exe, stage string) error {
		calls++
		if _, err := os.Stat(filepath.Join(root, "consented")); err != nil {
			t.Error("applied without visible consent")
		}
		return errors.New("denied UAC fixture")
	}
	driver := `import sys,json,pathlib,os
p=pathlib.Path(os.environ['SPACEWARS_LAUNCHER_FIXTURE']);phase=0
for line in sys.stdin:
 s=json.loads(line)
 if phase==0:
  assert s['secondary']=='Установить' and not s['busy'],s
  (p/'consented').write_text('explicit install')
  print('secondary',flush=True);phase=1
 elif s['message'].startswith('Не удалось установить'):
  assert s['primary']=='Играть сейчас' and not s['busy']
  (p/'retry-visible').write_text('ok');break
`
	launcherForHost = func(string) (*desktopLauncher, error) {
		return startLauncherCommand(exec.Command("python3", "-u", "-c", driver))
	}
	if err := host(root, filepath.Join(release, "UpdateHost")); err != nil {
		t.Fatal(err)
	}
	if calls != 1 {
		t.Fatalf("apply calls: %d", calls)
	}
	if _, err := os.Stat(filepath.Join(root, "retry-visible")); err != nil {
		t.Fatal(err)
	}
	if _, err := readPending(root, cache); err != nil {
		t.Fatal("pending update lost", err)
	}
	a, err := readActive(root)
	if err != nil || a.ID != current.ID {
		t.Fatal("working release changed", err)
	}
}
