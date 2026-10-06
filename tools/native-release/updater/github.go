package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"regexp"
	"strconv"
	"strings"
	"time"
)

const repository = "afonasev/spacewars"

var channel = "production"               // pinned into each helper; never selected by remote metadata
var apiOrigin = "https://api.github.com" // test injection only

type PackageChunk struct {
	Hash   string `json:"hash"`
	Size   int64  `json:"size"`
	Offset int64  `json:"offset"`
}
type Transport struct {
	Channel string         `json:"channel"`
	Tag     string         `json:"tag"`
	Asset   string         `json:"asset"`
	Size    int64          `json:"size"`
	Hash    string         `json:"sha256"`
	Chunks  []PackageChunk `json:"chunks"`
}

func versionParts(s string) ([]int64, error) {
	if !regexp.MustCompile(`^[0-9]+\.[0-9]+\.[0-9]+$`).MatchString(s) {
		return nil, errors.New("invalid version")
	}
	var result []int64
	for _, p := range strings.Split(s, ".") {
		n, e := strconv.ParseInt(p, 10, 32)
		if e != nil {
			return nil, e
		}
		result = append(result, n)
	}
	return result, nil
}
func newerVersion(a, b string) bool {
	aa, e := versionParts(a)
	bb, f := versionParts(b)
	if e != nil || f != nil {
		return false
	}
	for i := range aa {
		if aa[i] != bb[i] {
			return aa[i] > bb[i]
		}
	}
	return false
}
func packageURL(t *Transport) string {
	return "https://github.com/" + repository + "/releases/download/" + t.Tag + "/" + t.Asset
}
func validateTransport(m Manifest) error {
	t := m.Transport
	if t == nil {
		return nil
	} // schema1 installed/bridge compatibility only
	if t.Channel != "production" && t.Channel != "test" || t.Tag != "v"+m.Version || t.Asset != "Spacewars-"+m.Version+"-"+m.Platform+".pack" || t.Size <= 0 || t.Size > 4*1024*1024*1024 || !hashOK(t.Hash) {
		return errors.New("invalid signed transport")
	}
	expected := map[string]int64{}
	for _, f := range m.Files {
		for _, c := range f.Chunks {
			if n, ok := expected[c.Hash]; ok && n != c.Size {
				return errors.New("chunk size collision")
			}
			expected[c.Hash] = c.Size
		}
	}
	var offset int64
	seen := map[string]bool{}
	for _, c := range t.Chunks {
		if !hashOK(c.Hash) || seen[c.Hash] || expected[c.Hash] != c.Size || c.Offset != offset {
			return errors.New("invalid package layout")
		}
		seen[c.Hash] = true
		offset += c.Size
	}
	if offset != t.Size || len(seen) != len(expected) {
		return errors.New("incomplete package layout")
	}
	return nil
}
func githubHTTPClient() *http.Client {
	return &http.Client{Timeout: 60 * time.Second, CheckRedirect: func(r *http.Request, via []*http.Request) error {
		if len(via) > 4 {
			return errors.New("too many redirects")
		}
		first := via[0].URL
		if first.Host == r.URL.Host && first.Scheme == r.URL.Scheme {
			return nil
		}
		if first.Scheme == "https" && first.Host == "github.com" && r.URL.Scheme == "https" && (r.URL.Host == "release-assets.githubusercontent.com" || r.URL.Host == "objects.githubusercontent.com") {
			return nil
		}
		return errors.New("untrusted redirect")
	}}
}
func githubFetch(address string, limit int64) ([]byte, error) {
	req, e := http.NewRequest("GET", address, nil)
	if e != nil {
		return nil, e
	}
	req.Header.Set("User-Agent", "Spacewars-Updater")
	req.Header.Set("Accept", "application/vnd.github+json")
	r, e := githubHTTPClient().Do(req)
	if e != nil {
		return nil, e
	}
	defer r.Body.Close()
	if r.StatusCode != 200 {
		return nil, fmt.Errorf("GitHub HTTP %d", r.StatusCode)
	}
	b, e := io.ReadAll(io.LimitReader(r.Body, limit+1))
	if int64(len(b)) > limit {
		return nil, errors.New("response too large")
	}
	return b, e
}

type githubAsset struct {
	Name string `json:"name"`
	URL  string `json:"browser_download_url"`
	Size int64  `json:"size"`
}
type githubRelease struct {
	Tag        string        `json:"tag_name"`
	Draft      bool          `json:"draft"`
	Prerelease bool          `json:"prerelease"`
	Assets     []githubAsset `json:"assets"`
}

func selectedRelease(r githubRelease, wanted string) bool {
	return !r.Draft && (wanted == "test" && r.Prerelease || wanted == "production" && !r.Prerelease)
}
func githubCandidate(platform string) ([]byte, Manifest, error) {
	var releases []githubRelease
	for page := 1; page <= 10; page++ {
		b, e := githubFetch(fmt.Sprintf("%s/repos/%s/releases?per_page=100&page=%d", apiOrigin, repository, page), 16*1024*1024)
		if e != nil {
			return nil, Manifest{}, e
		}
		var batch []githubRelease
		if e = json.Unmarshal(b, &batch); e != nil {
			return nil, Manifest{}, e
		}
		releases = append(releases, batch...)
		if len(batch) < 100 {
			break
		}
	}
	var best Manifest
	var bestRaw []byte
	for _, r := range releases {
		if !selectedRelease(r, channel) {
			continue
		}
		name := "Spacewars-" + strings.TrimPrefix(r.Tag, "v") + "-" + platform + ".json"
		for _, a := range r.Assets {
			if a.Name != name {
				continue
			}
			expected := "https://github.com/" + repository + "/releases/download/" + r.Tag + "/" + name
			if a.URL != expected {
				return nil, best, errors.New("metadata URL rejected")
			}
			raw, e := githubFetch(a.URL, 16*1024*1024)
			if e != nil {
				return nil, best, e
			}
			m, e := verifyEnvelope(raw, platform)
			if e != nil {
				return nil, best, e
			}
			if m.Transport == nil || m.Transport.Channel != channel || m.Transport.Tag != r.Tag {
				return nil, best, errors.New("release channel/tag rejected")
			}
			complete := false
			for _, pack := range r.Assets {
				if pack.Name == m.Transport.Asset && pack.Size == m.Transport.Size && pack.URL == packageURL(m.Transport) {
					complete = true
				}
			}
			if !complete {
				return nil, best, errors.New("missing update package")
			}
			if best.Sequence == 0 || m.Sequence > best.Sequence && newerVersion(m.Version, best.Version) {
				best = m
				bestRaw = raw
			}
		}
	}
	if best.Sequence == 0 {
		return nil, best, errors.New("no compatible GitHub release in channel")
	}
	return bestRaw, best, nil
}
func fetchRange(address string, c PackageChunk, total int64) ([]byte, error) {
	// Callers derive URL only from validated signed fields and a pinned repository.
	u, e := url.Parse(address)
	if e != nil {
		return nil, e
	}
	if u.Scheme != "https" && u.Host != strings.TrimPrefix(apiOrigin, "http://") {
		return nil, errors.New("invalid package scheme")
	}
	req, e := http.NewRequest("GET", address, nil)
	if e != nil {
		return nil, e
	}
	req.Header.Set("Range", fmt.Sprintf("bytes=%d-%d", c.Offset, c.Offset+c.Size-1))
	req.Header.Set("User-Agent", "Spacewars-Updater")
	r, e := githubHTTPClient().Do(req)
	if e != nil {
		return nil, e
	}
	defer r.Body.Close()
	expected := fmt.Sprintf("bytes %d-%d/%d", c.Offset, c.Offset+c.Size-1, total)
	if r.StatusCode != 206 || r.Header.Get("Content-Range") != expected {
		return nil, errors.New("package range rejected")
	}
	b, e := io.ReadAll(io.LimitReader(r.Body, c.Size+1))
	if e != nil || int64(len(b)) != c.Size || digest(b) != c.Hash {
		return nil, errors.New("package chunk rejected")
	}
	return b, nil
}
func storeNamespace() string {
	if channel == "test" {
		return "native-test-v1"
	}
	return "native-v1"
}
