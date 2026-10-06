package main

import (
	"crypto/ed25519"
	"crypto/rand"
	"encoding/base64"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func transportSample() Manifest {
	m := sample()
	m.Transport = &Transport{Channel: "test", Tag: "v1.0.0", Asset: "Spacewars-1.0.0-windows-x64.pack", Size: m.Files[0].Size, Hash: m.Files[0].Hash, Chunks: []PackageChunk{{Hash: m.Files[0].Hash, Size: m.Files[0].Size}}}
	return m
}
func TestSignedTransportRejectsLayoutAndChannelMismatch(t *testing.T) {
	for _, mutate := range []func(*Manifest){func(m *Manifest) { m.Transport.Channel = "unknown" }, func(m *Manifest) { m.Transport.Tag = "v2.0.0" }, func(m *Manifest) { m.Transport.Asset = "other.pack" }, func(m *Manifest) { m.Transport.Size++ }, func(m *Manifest) { m.Transport.Chunks[0].Offset = 1 }, func(m *Manifest) { m.Transport.Chunks = append(m.Transport.Chunks, m.Transport.Chunks[0]) }} {
		m := transportSample()
		mutate(&m)
		if _, e := verifyEnvelope(signed(t, m), m.Platform); e == nil {
			t.Fatal("invalid transport accepted")
		}
	}
	for _, ch := range []string{"production", "test"} {
		for _, pre := range []bool{false, true} {
			r := githubRelease{Prerelease: pre}
			if selectedRelease(r, ch) != (pre == (ch == "test")) {
				t.Fatal("channel leaked")
			}
			r.Draft = true
			if selectedRelease(r, ch) {
				t.Fatal("draft accepted")
			}
		}
	}
}
func TestExactRangeAndInterruptedDownload(t *testing.T) {
	data := []byte("working game")
	pc := PackageChunk{Hash: digest(data), Size: int64(len(data))}
	old := apiOrigin
	defer func() { apiOrigin = old }()
	for _, variant := range []string{"good", "full", "wrong-range", "truncated", "corrupt"} {
		server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if r.Header.Get("Range") != "bytes=0-11" {
				t.Error("wrong request")
			}
			if variant == "full" {
				w.Write(data)
				return
			}
			cr := "bytes 0-11/12"
			if variant == "wrong-range" {
				cr = "bytes 0-11/99"
			}
			w.Header().Set("Content-Range", cr)
			w.WriteHeader(206)
			if variant == "truncated" {
				w.Write(data[:4])
			} else if variant == "corrupt" {
				w.Write([]byte("not the game"))
			} else {
				w.Write(data)
			}
		}))
		apiOrigin = server.URL
		_, e := fetchRange(server.URL, pc, 12)
		server.Close()
		if (e == nil) != (variant == "good") {
			t.Fatalf("%s: %v", variant, e)
		}
	}
}
func TestPackageRoundtripWholeHashAndLegacyBootstrap(t *testing.T) {
	input := t.TempDir()
	out := t.TempDir()
	keyFile := filepath.Join(t.TempDir(), "key")
	pub, key, _ := ed25519.GenerateKey(rand.Reader)
	publicKey = base64.StdEncoding.EncodeToString(pub)
	os.WriteFile(keyFile, []byte(base64.StdEncoding.EncodeToString(key)), 0600)
	os.WriteFile(filepath.Join(input, "Player.exe"), []byte("working game"), 0755)
	if e := pack([]string{input, out, keyFile, "windows-x64", "Player.exe", "1.0.1", "2", "abc", "test"}); e != nil {
		t.Fatal(e)
	}
	raw := filepath.Join(out, "github", "Spacewars-1.0.1-windows-x64.json")
	packPath := filepath.Join(out, "github", "Spacewars-1.0.1-windows-x64.pack")
	if e := verifyPackageFile(raw, "windows-x64", packPath); e != nil {
		t.Fatal(e)
	}
	// The original schema1 reader ignores optional transport while retaining trusted signature/files.
	b, _ := os.ReadFile(raw)
	m, e := verifyEnvelope(b, "windows-x64")
	if e != nil || m.Schema != 1 {
		t.Fatal("legacy schema broken", e)
	}
	os.WriteFile(packPath, []byte("not the game"), 0644)
	if e := verifyPackageFile(raw, "windows-x64", packPath); e == nil {
		t.Fatal("corrupt package accepted")
	}
}
func TestVersionAndReplay(t *testing.T) {
	if !newerVersion("1.0.10", "1.0.9") || newerVersion("1.0.1", "1.0.2") || newerVersion("1.0.0-test", "1.0.0") {
		t.Fatal("version comparison")
	}
	cache := t.TempDir()
	m := transportSample()
	m.Sequence = 5
	if e := rememberHighest(cache, m); e != nil {
		t.Fatal(e)
	}
	m.Sequence = 4
	if e := rememberHighest(cache, m); e == nil {
		t.Fatal("rollback seen")
	}
	old := channel
	channel = "production"
	defer func() { channel = old }()
	if e := writePending("root", cache, "stage", transportSample(), []byte("raw")); e == nil {
		t.Fatal("test pending in production")
	}
}
func TestRejectUntrustedRedirect(t *testing.T) {
	client := githubHTTPClient()
	req, _ := http.NewRequest("GET", "https://evil.example/package", nil)
	start, _ := http.NewRequest("GET", "https://github.com/x", nil)
	if e := client.CheckRedirect(req, []*http.Request{start}); e == nil {
		t.Fatal("untrusted CDN")
	}
	req.URL.Host = "release-assets.githubusercontent.com"
	if e := client.CheckRedirect(req, []*http.Request{start}); e != nil {
		t.Fatal(e)
	}
	req.URL.Scheme = "http"
	if e := client.CheckRedirect(req, []*http.Request{start}); e == nil {
		t.Fatal("TLS downgrade")
	}
	if strings.Contains(storeNamespace(), "production") {
		t.Fatal("legacy production namespace changed")
	}
}
