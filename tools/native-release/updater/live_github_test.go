package main

import (
	"encoding/json"
	"net/http"
	"os"
	"strings"
	"testing"
)

type liveRecorder struct {
	base     http.RoundTripper
	requests []string
}

func (r *liveRecorder) RoundTrip(q *http.Request) (*http.Response, error) {
	r.requests = append(r.requests, q.URL.Host+q.URL.Path)
	if q.Header.Get("Range") != "" {
		panic("package request during version check")
	}
	return r.base.RoundTrip(q)
}
func TestLiveGitHubCheckUsesMetadataOnlyAndIsolatesChannels(t *testing.T) {
	expected := os.Getenv("SPACEWARS_LIVE_VERSION")
	if expected == "" {
		t.Skip("explicit live GitHub check only")
	}
	b, e := os.ReadFile("../trust.json")
	if e != nil {
		t.Fatal(e)
	}
	var trust struct {
		PublicKey string `json:"public_key"`
	}
	if e = json.Unmarshal(b, &trust); e != nil {
		t.Fatal(e)
	}
	publicKey = trust.PublicKey
	old := http.DefaultTransport
	oldChannel := channel
	defer func() { http.DefaultTransport = old; channel = oldChannel }()
	r := &liveRecorder{base: old}
	http.DefaultTransport = r
	channel = "test"
	for _, platform := range []string{"windows-x64", "macos-universal"} {
		_, m, e := githubCandidate(platform)
		if e != nil {
			t.Fatal(e)
		}
		if m.Version != expected || m.Transport.Channel != "test" {
			t.Fatal("wrong live candidate")
		}
	}
	channel = "production"
	if _, m, e := githubCandidate("windows-x64"); e == nil && m.Version == expected {
		t.Fatal("test candidate leaked into production")
	}
	for _, url := range r.requests {
		if strings.Contains(url, ".pack") || strings.Contains(url, "afonasev.tech") {
			t.Fatal("nonmetadata/VPS request", url)
		}
	}
	t.Logf("verified signed %s candidate for both platforms; metadata requests=%d; package bytes=0; production did not select test", expected, len(r.requests))
}
