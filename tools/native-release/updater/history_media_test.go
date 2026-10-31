package main

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"image"
	"image/png"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func pngFixture() ([]byte, string) {
	var b bytes.Buffer
	_ = png.Encode(&b, image.NewRGBA(image.Rect(0, 0, 8, 4)))
	data := b.Bytes()
	sum := sha256.Sum256(data)
	return data, "notes-" + hex.EncodeToString(sum[:]) + ".png"
}
func TestHistoryBlocksRequireSameReleaseAttachmentAndKeepItemOrder(t *testing.T) {
	data, name := pngFixture()
	url := "https://github.com/" + repository + "/releases/download/v1.0.0/" + name
	release := githubRelease{Tag: "v1.0.0", Assets: []githubAsset{{Name: name, URL: url, Size: int64(len(data))}}}
	release.Body = "## Русский\n### Интерфейс\n- Панель\n\n  ![Панель](" + url + ")\n- Исправление\n## English\n### UI\n- Panel\n  ![Panel](" + url + ")"
	note := releaseNote(release)
	if len(note.BlocksRu) != 4 || note.BlocksRu[2].Asset != name || note.BlocksRu[3].Kind != "item" || note.BlocksEn[2].Text != "Panel" {
		t.Fatalf("wrong blocks: %+v", note)
	}
	for _, bad := range []string{strings.Replace(url, "v1.0.0", "v2.0.0", 1), "https://evil.invalid/" + name, "file:///tmp/" + name} {
		blocks := historyBlocks("![Caption]("+bad+")", release)
		if blocks[0].Kind != "text" || blocks[0].Asset != "" {
			t.Fatal("foreign image accepted")
		}
	}
	release.Assets = nil
	if historyBlocks("![Caption]("+url+")", release)[0].Kind != "text" {
		t.Fatal("unattached image accepted")
	}
}
func TestHistoryCacheOfflineMediaIntegrityAndReleaseAssociation(t *testing.T) {
	oldOrigin := apiOrigin
	defer func() { apiOrigin = oldOrigin }()
	data, name := pngFixture()
	url := "https://github.com/" + repository + "/releases/download/v1.0.0/" + name
	release := githubRelease{Tag: "v1.0.0", Prerelease: channel == "test", Body: "## Русский\n- Panel\n![Caption](" + url + ")\n## English\n- Panel", Assets: []githubAsset{{Name: name, URL: url, Size: int64(len(data))}}}
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { _ = json.NewEncoder(w).Encode([]githubRelease{release}) }))
	apiOrigin = server.URL
	root := t.TempDir()
	h, e := cachedHistory(context.Background(), root, "1.0.0")
	if e != nil || len(h.Releases) != 1 {
		t.Fatal(e)
	}
	server.Close()
	h, e = cachedHistory(context.Background(), root, "1.0.0")
	if e != nil || h.Releases[0].BlocksRu[1].Asset != name {
		t.Fatal("offline history lost", e)
	}
	media := filepath.Join(historyDirectory(root), "media")
	_ = os.MkdirAll(media, 0700)
	_ = os.WriteFile(filepath.Join(media, name), data, 0600)
	got, e := historyImage(context.Background(), root, "1.0.0", "1.0.0", name)
	if e != nil || !bytes.Equal(got, data) {
		t.Fatal("offline media lost", e)
	}
	for _, version := range []string{"2.0.0", "../1.0.0"} {
		if _, e = historyImage(context.Background(), root, "1.0.0", version, name); e == nil {
			t.Fatal("foreign release accepted")
		}
	}
	if _, e = readHistoryCache(root, "9.0.0"); e == nil {
		t.Fatal("cache without installed version accepted")
	}
	broken := append([]byte{}, data...)
	broken[len(broken)-1] ^= 1
	if validateHistoryImage(broken, name) == nil || validateHistoryImage(data, "../"+name) == nil {
		t.Fatal("corrupt/path media accepted")
	}
}
func TestCancelledHistoryDoesNotReplaceGoodCache(t *testing.T) {
	root := t.TempDir()
	dir := historyDirectory(root)
	_ = os.MkdirAll(dir, 0700)
	good := ReleaseHistory{Releases: []ReleaseNote{{Version: "1.0.0", Russian: "Cached"}}}
	_ = atomicJSON(filepath.Join(dir, "history.json"), good)
	old := apiOrigin
	apiOrigin = "http://127.0.0.1:1"
	defer func() { apiOrigin = old }()
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if _, e := cachedHistory(ctx, root, "1.0.0"); e == nil {
		t.Fatal("cancellation swallowed")
	}
	h, e := readHistoryCache(root, "1.0.0")
	if e != nil || h.Releases[0].Russian != "Cached" {
		t.Fatal("cache overwritten")
	}
}

type historyRoundTrip func(*http.Request) (*http.Response, error)

func (f historyRoundTrip) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }
func TestHistoryImageDownloadsChecksHashAndPersistsForOffline(t *testing.T) {
	root := t.TempDir()
	dir := historyDirectory(root)
	_ = os.MkdirAll(dir, 0700)
	data, name := pngFixture()
	_ = atomicJSON(filepath.Join(dir, "history.json"), ReleaseHistory{Releases: []ReleaseNote{{Version: "1.0.0", BlocksRu: []HistoryBlock{{Kind: "image", Asset: name}}}}})
	previous := http.DefaultTransport
	defer func() { http.DefaultTransport = previous }()
	calls := 0
	http.DefaultTransport = historyRoundTrip(func(r *http.Request) (*http.Response, error) {
		calls++
		if r.URL.String() != "https://github.com/"+repository+"/releases/download/v1.0.0/"+name {
			t.Fatal("unexpected image URL", r.URL)
		}
		return &http.Response{StatusCode: 200, Body: io.NopCloser(bytes.NewReader(data)), Header: make(http.Header)}, nil
	})
	got, e := historyImage(context.Background(), root, "1.0.0", "1.0.0", name)
	if e != nil || !bytes.Equal(got, data) {
		t.Fatal("image download failed", e)
	}
	http.DefaultTransport = historyRoundTrip(func(r *http.Request) (*http.Response, error) {
		t.Fatal("cached image attempted network")
		return nil, errors.New("offline")
	})
	if _, e = historyImage(context.Background(), root, "1.0.0", "1.0.0", name); e != nil || calls != 1 {
		t.Fatal("image was not cached", e)
	}
	_ = os.WriteFile(filepath.Join(dir, "media", name), []byte("corrupt cache"), 0600)
	http.DefaultTransport = historyRoundTrip(func(r *http.Request) (*http.Response, error) {
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader("wrong bytes")), Header: make(http.Header)}, nil
	})
	if _, e = historyImage(context.Background(), root, "1.0.0", "1.0.0", name); e == nil {
		t.Fatal("corrupt network image accepted")
	}
	if b, _ := os.ReadFile(filepath.Join(dir, "media", name)); string(b) != "corrupt cache" {
		t.Fatal("failed media replaced cache")
	}
}
