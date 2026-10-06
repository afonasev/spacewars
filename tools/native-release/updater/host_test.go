package main

import (
	"crypto/ed25519"
	"crypto/rand"
	"encoding/base64"
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestHostExplicitBackgroundAndExitActivation(t *testing.T) {
	oldChannel := channel
	oldDiscover := discoverForHost
	oldStage := stageForHost
	oldApply := applyForHost
	defer func() {
		channel = oldChannel
		discoverForHost = oldDiscover
		stageForHost = oldStage
		applyForHost = oldApply
	}()
	channel = "test"
	for _, deny := range []bool{false, true} {
		root := t.TempDir()
		base, _ := os.UserCacheDir()
		cache := filepath.Join(base, "Spacewars", storeNamespace(), digest([]byte(root)))
		defer os.RemoveAll(cache)
		t.Setenv("SPACEWARS_FIXTURE_ROOT", root)
		pub, key, _ := ed25519.GenerateKey(rand.Reader)
		publicKey = base64.StdEncoding.EncodeToString(pub)
		script := []byte(`#!/usr/bin/env python3
import os,json,time,urllib.request,pathlib
base=os.environ['SPACEWARS_UPDATE_ENDPOINT'];token=os.environ['SPACEWARS_UPDATE_TOKEN'];root=pathlib.Path(os.environ['SPACEWARS_FIXTURE_ROOT'])
def req(path,post=False):
 r=urllib.request.Request(base+path,method='POST' if post else 'GET',headers={'Authorization':'Bearer '+token})
 return json.load(urllib.request.urlopen(r))
req('/ready',True)
for i in range(200):
 s=req('/status')
 if s['state']=='available':break
 time.sleep(.02)
assert s['state']=='available'
assert not (root/'download-started').exists(), 'download before explicit click'
old=(root/'active.json').read_text();req('/update',True);s=req('/play',True)
assert s['state']=='downloading', 'play blocked during download'
for i in range(200):
 s=req('/status')
 assert (root/'active.json').read_text()==old,'activation during play'
 if s['state']=='prepared':break
 time.sleep(.02)
assert s['state']=='prepared'
(root/'player-exiting').write_text('normal exit')
`)
		current := sample()
		current.Platform = platformID()
		current.Files[0].Size = int64(len(script))
		current.Files[0].Hash = digest(script)
		current.Files[0].Chunks = []Chunk{{Hash: digest(script), Size: int64(len(script))}}
		release := filepath.Join(root, "releases", current.ID)
		os.MkdirAll(release, 0755)
		os.WriteFile(filepath.Join(release, current.Entry), script, 0755)
		os.WriteFile(filepath.Join(release, "manifest.json"), signWith(current, key), 0644)
		atomicJSON(filepath.Join(root, "active.json"), Active{ID: current.ID})
		next := transportSample()
		next.Platform = platformID()
		next.Sequence = 2
		next.Version = "1.0.1"
		next.ID = digest([]byte("next"))
		next.Transport.Tag = "v1.0.1"
		next.Transport.Asset = "Spacewars-1.0.1-" + next.Platform + ".pack"
		raw := signWith(next, key)
		discoverForHost = func(p string) ([]byte, Manifest, error) { return raw, next, nil }
		stageForHost = func(r, c string, m Manifest, b []byte, progress func(int64, int64)) (string, error) {
			os.WriteFile(filepath.Join(root, "download-started"), []byte("1"), 0600)
			progress(0, 12)
			time.Sleep(300 * time.Millisecond)
			s := filepath.Join(c, "staged", m.ID)
			os.MkdirAll(s, 0700)
			os.WriteFile(filepath.Join(s, m.Entry), []byte("working game"), 0755)
			os.WriteFile(filepath.Join(s, "manifest.json"), b, 0600)
			progress(12, 12)
			return s, nil
		}
		applied := false
		applyForHost = func(exe, stage string) error {
			if _, e := os.Stat(filepath.Join(root, "player-exiting")); e != nil {
				t.Error("apply before exit")
			}
			applied = true
			if deny {
				return errors.New("simulated denied UAC")
			}
			return applyStage(root, stage, platformID())
		}
		if e := host(root, filepath.Join(release, "UpdateHost")); e != nil {
			t.Fatal(e)
		}
		active, e := readActive(root)
		if e != nil {
			t.Fatal(e)
		}
		if !applied {
			t.Fatal("normal exit did not apply prepared update")
		}
		if deny && active.ID != current.ID || !deny && active.ID != next.ID {
			t.Fatal("wrong pointer after exit/denial")
		}
		if deny {
			stage, e := readPending(root, cache)
			if e != nil || stage == "" {
				t.Fatal("denied apply lost durable pending", e)
			}
			applyForHost = func(exe, stage string) error { return applyStage(root, stage, platformID()) }
			if e := host(root, filepath.Join(release, "UpdateHost")); !errors.Is(e, restartRequested) {
				t.Fatal("next launch did not activate pending", e)
			}
			active, _ := readActive(root)
			if active.ID != next.ID {
				t.Fatal("next launch pointer")
			}
		}
	}
}
