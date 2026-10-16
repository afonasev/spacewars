package main

import (
	"context"
	"crypto/ed25519"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"
)

func TestApplicationArgumentsPreserveSilentAndCustomFlags(t *testing.T) {
	want := []string{"-noaudio", "-muted", "-logFile", "/a path/player.log"}
	got := applicationArgs(append([]string{"--host"}, want...))
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("arguments lost: %v", got)
	}
}
func TestHistoryIncludesInstalledReleaseWithoutDiscoveringCandidate(t *testing.T) {
	oldOrigin, oldChannel := apiOrigin, channel
	defer func() { apiOrigin = oldOrigin; channel = oldChannel }()
	channel = "production"
	calls := []string{}
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		calls = append(calls, r.URL.Path)
		if strings.Contains(r.URL.Path, "/tags/") {
			json.NewEncoder(w).Encode(githubRelease{Tag: "v1.0.0", Published: "2026-01-01", Body: "## Русский\nСтарая версия.\n## English\nOld version."})
			return
		}
		json.NewEncoder(w).Encode([]githubRelease{{Tag: "v2.0.0", Published: "2026-10-15", Body: "## Русский\nНовое меню.\n## English\nNew menu."}, {Tag: "v3.0.0", Draft: true}})
	}))
	defer server.Close()
	apiOrigin = server.URL
	history, err := githubHistory("1.0.0")
	if err != nil {
		t.Fatal(err)
	}
	if len(history.Releases) != 2 || history.Releases[1].Version != "1.0.0" || history.Releases[0].Russian != "Новое меню." || history.Releases[0].English != "New menu." || history.Releases[1].Date != "2026-01-01" {
		t.Fatalf("wrong history: %+v", history)
	}
	if len(calls) != 2 {
		t.Fatalf("unexpected discovery requests: %v", calls)
	}
	legacy := releaseNote(githubRelease{Tag: "v0.1.0", Body: "legacy english text"})
	if legacy.Russian != "" || legacy.Date != "" {
		t.Fatal("invented historical translation or date")
	}
}
func TestRangeRequestStopsOnMatchCancellation(t *testing.T) {
	started := make(chan struct{})
	stopped := make(chan struct{})
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { close(started); <-r.Context().Done(); close(stopped) }))
	defer server.Close()
	old := apiOrigin
	apiOrigin = server.URL
	defer func() { apiOrigin = old }()
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error)
	go func() { _, err := fetchRangeContext(ctx, server.URL, PackageChunk{Size: 1}, 1); done <- err }()
	<-started
	cancel()
	if err := <-done; err == nil {
		t.Fatal("cancelled download accepted")
	}
	select {
	case <-stopped:
	case <-time.After(time.Second):
		t.Fatal("network request did not stop")
	}
}
func installerFixture(t *testing.T, version string, sequence int64, key ed25519.PrivateKey) (string, Manifest) {
	t.Helper()
	payload := t.TempDir()
	m := sample()
	m.Platform = platformID()
	m.Version = version
	m.Sequence = sequence
	m.ID = digest([]byte(version))
	helper := []byte("signed helper " + version)
	m.Files = append(m.Files, File{Path: executableName("UpdateHost"), Size: int64(len(helper)), Hash: digest(helper), Mode: 0755, Chunks: []Chunk{{Hash: digest(helper), Size: int64(len(helper))}}})
	release := filepath.Join(payload, "releases", m.ID)
	os.MkdirAll(release, 0755)
	os.WriteFile(filepath.Join(release, m.Entry), []byte("working game"), 0755)
	os.WriteFile(filepath.Join(release, executableName("UpdateHost")), helper, 0755)
	os.WriteFile(filepath.Join(payload, executableName("Spacewars")), helper, 0755)
	os.WriteFile(filepath.Join(release, "manifest.json"), signWith(m, key), 0644)
	return payload, m
}
func TestInstallerTransactionReinstallDowngradeAndCorruptPayload(t *testing.T) {
	oldKey := publicKey
	defer func() { publicKey = oldKey }()
	pub, key, _ := ed25519.GenerateKey(rand.Reader)
	publicKey = base64.StdEncoding.EncodeToString(pub)
	root := t.TempDir()
	base, _ := os.UserCacheDir()
	defer os.RemoveAll(filepath.Join(base, "Spacewars", storeNamespace(), digest([]byte(root))))
	p, m := installerFixture(t, "1.0.0", 1, key)
	if err := installPayload(root, p, m.ID); err != nil {
		t.Fatal(err)
	}
	os.WriteFile(filepath.Join(root, "releases", m.ID, m.Entry), []byte("damaged"), 0755)
	if err := installPayload(root, p, m.ID); err != nil {
		t.Fatal("reinstall repair:", err)
	}
	if err := verifyReleaseFiles(root, m); err != nil {
		t.Fatal(err)
	}
	p2, m2 := installerFixture(t, "1.0.1", 2, key)
	if err := installPayload(root, p2, m2.ID); err != nil {
		t.Fatal(err)
	}
	before, _ := os.ReadFile(filepath.Join(root, "active.json"))
	if err := installPayload(root, p, m.ID); err == nil {
		t.Fatal("downgrade allowed")
	}
	p3, m3 := installerFixture(t, "1.0.2", 3, key)
	os.WriteFile(filepath.Join(p3, "releases", m3.ID, m3.Entry), []byte("tampered"), 0755)
	if err := installPayload(root, p3, m3.ID); err == nil {
		t.Fatal("corrupt payload installed")
	}
	after, _ := os.ReadFile(filepath.Join(root, "active.json"))
	if string(before) != string(after) {
		t.Fatal("failed installation changed active pointer")
	}
	if err := requireSpace(root, ^uint64(0)); err == nil {
		t.Fatal("impossible disk requirement accepted")
	}
}

func TestInstallerRefusesAnActiveHostBeforeWriting(t *testing.T) {
	oldKey := publicKey
	defer func() { publicKey = oldKey }()
	pub, key, _ := ed25519.GenerateKey(rand.Reader)
	publicKey = base64.StdEncoding.EncodeToString(pub)
	root := t.TempDir()
	payload, m := installerFixture(t, "1.0.0", 1, key)
	base, _ := os.UserCacheDir()
	cache := filepath.Join(base, "Spacewars", storeNamespace(), digest([]byte(root)))
	os.MkdirAll(cache, 0700)
	defer os.RemoveAll(cache)
	unlock, err := acquireHostLock(root, cache)
	if err != nil {
		t.Fatal(err)
	}
	if err = installPayload(root, payload, m.ID); err == nil {
		unlock()
		t.Fatal("installer changed running game")
	}
	unlock()
	if _, err = os.Stat(filepath.Join(root, "active.json")); !os.IsNotExist(err) {
		t.Fatal("failed preflight modified active installation")
	}
}
