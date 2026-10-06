package main

import (
	"crypto/ed25519"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func signed(t *testing.T, m Manifest) []byte {
	t.Helper()
	pub, key, e := ed25519.GenerateKey(rand.Reader)
	if e != nil {
		t.Fatal(e)
	}
	publicKey = base64.StdEncoding.EncodeToString(pub)
	return signWith(m, key)
}
func signWith(m Manifest, key ed25519.PrivateKey) []byte {
	b, _ := json.Marshal(m)
	r, _ := json.Marshal(Envelope{Payload: base64.StdEncoding.EncodeToString(b), Signature: base64.StdEncoding.EncodeToString(ed25519.Sign(key, b))})
	return r
}
func sample() Manifest {
	b := []byte("working game")
	return Manifest{Schema: 1, Sequence: 1, Version: "1.0.0", ID: digest([]byte("release1")), Platform: "windows-x64", Entry: "Player.exe", Files: []File{{Path: "Player.exe", Size: int64(len(b)), Hash: digest(b), Mode: 0755, Chunks: []Chunk{{Hash: digest(b), Size: int64(len(b))}}}}}
}
func TestSignedManifestRejectsTamperAndWrongPlatform(t *testing.T) {
	m := sample()
	raw := signed(t, m)
	if _, e := verifyEnvelope(raw, m.Platform); e != nil {
		t.Fatal(e)
	}
	if _, e := verifyEnvelope(raw, "macos-universal"); e == nil {
		t.Fatal("wrong platform accepted")
	}
	var env Envelope
	_ = json.Unmarshal(raw, &env)
	payload, _ := base64.StdEncoding.DecodeString(env.Payload)
	payload = append(payload, ' ')
	env.Payload = base64.StdEncoding.EncodeToString(payload)
	raw, _ = json.Marshal(env)
	if _, e := verifyEnvelope(raw, m.Platform); e == nil {
		t.Fatal("tampered payload accepted")
	}
}
func TestSignedManifestRejectsUnsafePathsAndCollisions(t *testing.T) {
	for _, name := range []string{"../Player.exe", "/etc/file", "C:\\file", "folder/../file", "CON.txt", "dir\\file", "a.", "AUX"} {
		m := sample()
		m.Entry = name
		m.Files[0].Path = name
		if _, e := verifyEnvelope(signed(t, m), m.Platform); e == nil {
			t.Errorf("accepted %q", name)
		}
	}
	m := sample()
	f := m.Files[0]
	f.Path = "player.EXE"
	m.Files = append(m.Files, f)
	if _, e := verifyEnvelope(signed(t, m), m.Platform); e == nil {
		t.Fatal("case collision accepted")
	}
}
func TestChangedChunksReuseAndVerifiedActivation(t *testing.T) {
	root := t.TempDir()
	cache := t.TempDir()
	old := sample()
	pub, key, _ := ed25519.GenerateKey(rand.Reader)
	publicKey = base64.StdEncoding.EncodeToString(pub)
	rawOld := signWith(old, key)
	dir := filepath.Join(root, "releases", old.ID)
	_ = os.MkdirAll(dir, 0755)
	_ = os.WriteFile(filepath.Join(dir, "Player.exe"), []byte("working game"), 0755)
	_ = os.WriteFile(filepath.Join(dir, "manifest.json"), rawOld, 0644)
	if e := atomicJSON(filepath.Join(root, "active.json"), Active{ID: old.ID}); e != nil {
		t.Fatal(e)
	}
	unchanged := []byte("working game")
	changed := []byte(" new asset")
	next := old
	next.ID = digest([]byte("release2"))
	next.Sequence = 2
	next.Version = "1.0.1"
	all := append(append([]byte(nil), unchanged...), changed...)
	next.Files = []File{{Path: "Player.exe", Size: int64(len(all)), Hash: digest(all), Mode: 0755, Chunks: []Chunk{{Hash: digest(unchanged), Size: int64(len(unchanged))}, {Hash: digest(changed), Size: int64(len(changed))}}}}
	requests := 0
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		requests++
		if strings.HasSuffix(r.URL.Path, digest(changed)) {
			w.Write(changed)
		} else {
			t.Errorf("unexpected unchanged request: %s", r.URL.Path)
			http.Error(w, "unexpected", 404)
		}
	}))
	defer srv.Close()
	prior := origin
	origin = srv.URL
	defer func() { origin = prior }()
	if requests != 0 {
		t.Fatal("requests before consent")
	}
	stage, e := stageUpdate(root, cache, next, signWith(next, key), func(int64, int64) {})
	if e != nil {
		t.Fatal(e)
	}
	if requests != 1 {
		t.Fatalf("requested %d chunks; want exactly one changed chunk", requests)
	}
	// Tampered staging must preserve the active pointer.
	p := filepath.Join(stage, "Player.exe")
	_ = os.WriteFile(p, []byte("tampered"), 0755)
	if e = applyStage(root, stage, next.Platform); e == nil {
		t.Fatal("tampered stage activated")
	}
	a, _ := readActive(root)
	if a.ID != old.ID {
		t.Fatal("old version lost")
	}
	_ = os.WriteFile(p, all, 0755)
	if e = applyStage(root, stage, next.Platform); e != nil {
		t.Fatal(e)
	}
	a, _ = readActive(root)
	if a.ID != next.ID || a.Previous != old.ID {
		t.Fatal("bad active/rollback identity")
	}
	if e = applyStage(root, stage, next.Platform); e == nil {
		t.Fatal("replayed sequence accepted")
	}
	if _, e = os.Stat(filepath.Join(root, "releases", old.ID, "Player.exe")); e != nil {
		t.Fatal("working rollback release removed")
	}
}
func TestInterruptedDownloadPreservesInstalledVersion(t *testing.T) {
	root := t.TempDir()
	cache := t.TempDir()
	m := sample()
	raw := signed(t, m)
	dir := filepath.Join(root, "releases", m.ID)
	_ = os.MkdirAll(dir, 0755)
	_ = os.WriteFile(filepath.Join(dir, "manifest.json"), raw, 0644)
	_ = atomicJSON(filepath.Join(root, "active.json"), Active{ID: m.ID})
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { http.Error(w, "offline", 503) }))
	defer srv.Close()
	prior := origin
	origin = srv.URL
	defer func() { origin = prior }()
	if _, e := stageUpdate(root, cache, m, raw, func(int64, int64) {}); e == nil {
		t.Fatal("failed download accepted")
	}
	a, _ := readActive(root)
	if a.ID != m.ID {
		t.Fatal("changed active during download")
	}
}
func TestProcessLockRecoversWithExistingLockFile(t *testing.T) {
	root := t.TempDir()
	if e := os.WriteFile(filepath.Join(root, "apply.lock"), []byte("left by interrupted older updater"), 0600); e != nil {
		t.Fatal(e)
	}
	unlock, e := acquireApplyLock(root)
	if e != nil {
		t.Fatal("stale marker must not block recovery", e)
	}
	if other, e := acquireApplyLock(root); e == nil {
		other()
		t.Fatal("concurrent writer admitted")
	}
	unlock()
	unlock, e = acquireApplyLock(root)
	if e != nil {
		t.Fatal("lock not released", e)
	}
	unlock()
}
func TestReadinessSeparatesBootstrapFailureFromGameplayCrash(t *testing.T) {
	failure := fmt.Errorf("Player failed")
	if classifyPlayerExit(false, failure) != failure {
		t.Fatal("bootstrap failure lost")
	}
	if classifyPlayerExit(true, failure) != crashAfterReady {
		t.Fatal("ready Player crash could trigger bootstrap rollback")
	}
	if classifyPlayerExit(false, nil) != nil || classifyPlayerExit(true, nil) != nil {
		t.Fatal("voluntary exit must not trigger recovery")
	}
}

func TestInstallerActivationPreservesPointerOnTamperAndDowngrade(t *testing.T) {
	root := t.TempDir()
	pub, key, _ := ed25519.GenerateKey(rand.Reader)
	publicKey = base64.StdEncoding.EncodeToString(pub)
	write := func(m Manifest) {
		dir := filepath.Join(root, "releases", m.ID)
		if e := os.MkdirAll(dir, 0755); e != nil {
			t.Fatal(e)
		}
		os.WriteFile(filepath.Join(dir, "Player.exe"), []byte("working game"), 0755)
		os.WriteFile(filepath.Join(dir, "manifest.json"), signWith(m, key), 0644)
	}
	old := sample()
	write(old)
	if e := activateInstalled(root, old.ID, old.Platform); e != nil {
		t.Fatal(e)
	}
	next := sample()
	next.ID = digest([]byte("new installer"))
	next.Sequence = 2
	write(next)
	os.WriteFile(filepath.Join(root, "releases", next.ID, "Player.exe"), []byte("tampered"), 0755)
	if e := activateInstalled(root, next.ID, next.Platform); e == nil {
		t.Fatal("tamper accepted")
	}
	a, _ := readActive(root)
	if a.ID != old.ID {
		t.Fatal("old pointer lost")
	}
	write(next)
	if e := activateInstalled(root, next.ID, next.Platform); e != nil {
		t.Fatal(e)
	}
	a, _ = readActive(root)
	if a.ID != next.ID || a.Previous != old.ID {
		t.Fatal("recovery pointer missing")
	}
	if e := activateInstalled(root, old.ID, old.Platform); e == nil {
		t.Fatal("downgrade accepted")
	}
	if e := activateInstalled(root, next.ID, next.Platform); e != nil {
		t.Fatal("idempotent reinstall failed", e)
	}
}
