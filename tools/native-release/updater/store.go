package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type Active struct {
	ID       string `json:"id"`
	Previous string `json:"previous,omitempty"`
}

func readActive(root string) (Active, error) {
	var a Active
	b, e := os.ReadFile(filepath.Join(root, "active.json"))
	if e != nil {
		return a, e
	}
	e = json.Unmarshal(b, &a)
	if !hashOK(a.ID) {
		return a, errors.New("invalid installed identity")
	}
	return a, e
}
func atomicJSON(name string, v any) error {
	b, e := json.Marshal(v)
	if e != nil {
		return e
	}
	tmp := name + ".tmp"
	if e = os.WriteFile(tmp, b, 0600); e != nil {
		return e
	}
	return replaceFile(tmp, name)
}
func installed(root, id, platform string) (Manifest, error) {
	b, e := os.ReadFile(filepath.Join(root, "releases", id, "manifest.json"))
	if e != nil {
		return Manifest{}, e
	}
	m, e := verifyEnvelope(b, platform)
	if e == nil && m.ID != id {
		e = errors.New("installed release identity mismatch")
	}
	return m, e
}
func regular(p string) error {
	st, e := os.Lstat(p)
	if e != nil {
		return e
	}
	if !st.Mode().IsRegular() {
		return errors.New("expected regular file")
	}
	return nil
}
func fetch(url string, limit int64) ([]byte, error) {
	c := &http.Client{Timeout: 60 * time.Second, CheckRedirect: func(r *http.Request, via []*http.Request) error {
		if len(via) > 3 || r.URL.Scheme != "https" && r.URL.Host != via[0].URL.Host || r.URL.Host != via[0].URL.Host {
			return errors.New("redirect rejected")
		}
		return nil
	}}
	r, e := c.Get(url)
	if e != nil {
		return nil, e
	}
	defer r.Body.Close()
	if r.StatusCode != 200 {
		return nil, fmt.Errorf("HTTP %d", r.StatusCode)
	}
	b, e := io.ReadAll(io.LimitReader(r.Body, limit+1))
	if int64(len(b)) > limit {
		return nil, errors.New("response too large")
	}
	return b, e
}
func candidate(platform string) ([]byte, Manifest, error) {
	if !strings.HasPrefix(origin, "http://127.0.0.1:") {
		return githubCandidate(platform)
	}
	b, e := fetch(origin+"/"+platform+"/latest.json", 16*1024*1024)
	if e != nil {
		return nil, Manifest{}, e
	}
	m, e := verifyEnvelope(b, platform)
	return b, m, e
}
func copyVerified(from, to string, f File) error {
	if e := regular(from); e != nil {
		return e
	}
	src, e := os.Open(from)
	if e != nil {
		return e
	}
	defer src.Close()
	if e = os.MkdirAll(filepath.Dir(to), 0755); e != nil {
		return e
	}
	out, e := os.OpenFile(to, os.O_CREATE|os.O_EXCL|os.O_WRONLY, os.FileMode(f.Mode))
	if e != nil {
		return e
	}
	h := newHasher()
	n, e := io.Copy(io.MultiWriter(out, h), io.LimitReader(src, f.Size+1))
	if e != nil || n != f.Size || fmt.Sprintf("%x", h.Sum(nil)) != f.Hash {
		out.Close()
		return errors.New("staged file integrity rejected")
	}
	e = out.Sync()
	out.Close()
	return e
}
func stageUpdate(root, cache string, m Manifest, raw []byte, progress func(int64, int64)) (string, error) {
	stage := filepath.Join(cache, "staged", m.ID)
	if e := os.MkdirAll(stage, 0700); e != nil {
		return "", e
	}
	objects := filepath.Join(cache, "objects")
	if e := os.MkdirAll(objects, 0700); e != nil {
		return "", e
	}
	// Seed the cache from the installed release. Hash every reused chunk.
	a, e := readActive(root)
	if e != nil {
		return "", e
	}
	old, e := installed(root, a.ID, m.Platform)
	if e != nil {
		return "", e
	}
	for _, f := range old.Files {
		p := filepath.Join(root, "releases", a.ID, filepath.FromSlash(f.Path))
		if regular(p) != nil {
			continue
		}
		in, e := os.Open(p)
		if e != nil {
			continue
		}
		for _, c := range f.Chunks {
			b := make([]byte, c.Size)
			_, e = io.ReadFull(in, b)
			if e != nil {
				break
			}
			if digest(b) == c.Hash {
				op := filepath.Join(objects, c.Hash)
				if regular(op) != nil {
					_ = os.WriteFile(op, b, 0600)
				}
			}
		}
		in.Close()
	}
	var total, done int64
	unique := map[string]Chunk{}
	for _, f := range m.Files {
		for _, c := range f.Chunks {
			if _, ok := unique[c.Hash]; !ok {
				unique[c.Hash] = c
				total += c.Size
			}
		}
	}
	progress(0, total)
	for _, f := range m.Files {
		p := filepath.Join(stage, filepath.FromSlash(f.Path))
		if e = os.MkdirAll(filepath.Dir(p), 0700); e != nil {
			return "", e
		}
		out, e := os.OpenFile(p, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, os.FileMode(f.Mode))
		if e != nil {
			return "", e
		}
		for _, c := range f.Chunks {
			op := filepath.Join(objects, c.Hash)
			b, e := os.ReadFile(op)
			if e != nil || int64(len(b)) != c.Size || digest(b) != c.Hash {
				if m.Transport != nil {
					var pc PackageChunk
					for _, x := range m.Transport.Chunks {
						if x.Hash == c.Hash {
							pc = x
							break
						}
					}
					b, e = fetchRange(packageURL(m.Transport), pc, m.Transport.Size)
				} else {
					b, e = fetch(origin+"/objects/"+c.Hash, c.Size)
				}
				if e != nil || int64(len(b)) != c.Size || digest(b) != c.Hash {
					out.Close()
					return "", errors.New("downloaded chunk rejected")
				}
				if e = os.WriteFile(op, b, 0600); e != nil {
					out.Close()
					return "", e
				}
			}
			if _, e = out.Write(b); e != nil {
				out.Close()
				return "", e
			}
			if _, ok := unique[c.Hash]; ok {
				done += c.Size
				delete(unique, c.Hash)
				progress(done, total)
			}
		}
		if e = out.Sync(); e != nil {
			out.Close()
			return "", e
		}
		out.Close()
		_ = os.Chmod(p, os.FileMode(f.Mode))
	}
	if m.Transport != nil {
		h := newHasher()
		for _, c := range m.Transport.Chunks {
			b, e := os.ReadFile(filepath.Join(objects, c.Hash))
			if e != nil || int64(len(b)) != c.Size || digest(b) != c.Hash {
				return "", errors.New("package reconstruction rejected")
			}
			h.Write(b)
		}
		if fmt.Sprintf("%x", h.Sum(nil)) != m.Transport.Hash {
			return "", errors.New("whole package hash rejected")
		}
	}
	if e = os.WriteFile(filepath.Join(stage, "manifest.json"), raw, 0600); e != nil {
		return "", e
	}
	return stage, nil
}
func applyStage(root, stage, platform string) error {
	// This method also runs in the privileged helper. No trust in caller metadata.
	raw, e := os.ReadFile(filepath.Join(stage, "manifest.json"))
	if e != nil {
		return e
	}
	m, e := verifyEnvelope(raw, platform)
	if e != nil {
		return e
	}
	if e = verifyStagedPackage(stage, m); e != nil {
		return e
	}
	unlock, e := acquireApplyLock(root)
	if e != nil {
		return e
	}
	defer unlock()
	a, e := readActive(root)
	if e != nil {
		return e
	}
	old, e := installed(root, a.ID, platform)
	if e != nil {
		return e
	}
	if m.Transport != nil && m.Transport.Channel != channel {
		return errors.New("apply channel rejected")
	}
	if m.Sequence <= old.Sequence || !newerVersion(m.Version, old.Version) {
		return errors.New("release rollback rejected")
	}
	releases := filepath.Join(root, "releases")
	st, e := os.Lstat(releases)
	if e != nil || !st.IsDir() || st.Mode()&os.ModeSymlink != 0 {
		return errors.New("invalid install root")
	}
	dst, e := os.MkdirTemp(releases, ".stage-")
	if e != nil {
		return e
	}
	defer os.RemoveAll(dst)
	for _, f := range m.Files {
		if e = copyVerified(filepath.Join(stage, filepath.FromSlash(f.Path)), filepath.Join(dst, filepath.FromSlash(f.Path)), f); e != nil {
			return e
		}
	}
	if e = os.WriteFile(filepath.Join(dst, "manifest.json"), raw, 0644); e != nil {
		return e
	}
	final := filepath.Join(releases, m.ID)
	if _, e = os.Lstat(final); e == nil {
		// Recover an immutable candidate left after a failed pointer commit.
		for _, f := range m.Files {
			p := filepath.Join(final, filepath.FromSlash(f.Path))
			if regular(p) != nil {
				return errors.New("invalid recovery file")
			}
			in, e := os.Open(p)
			if e != nil {
				return e
			}
			h := newHasher()
			n, e := io.Copy(h, io.LimitReader(in, f.Size+1))
			in.Close()
			if e != nil || n != f.Size || fmt.Sprintf("%x", h.Sum(nil)) != f.Hash {
				return errors.New("invalid recovery content")
			}
		}
	} else if !os.IsNotExist(e) {
		return e
	} else if e = os.Rename(dst, final); e != nil {
		return e
	}
	if e = atomicJSON(filepath.Join(root, "active.json"), Active{ID: m.ID, Previous: a.ID}); e != nil {
		return e
	}
	return nil
}
func envelopeEqual(a, b []byte) bool { return bytes.Equal(a, b) }

func verifyReleaseFiles(root string, m Manifest) error {
	for _, f := range m.Files {
		p := filepath.Join(root, "releases", m.ID, filepath.FromSlash(f.Path))
		if e := regular(p); e != nil {
			return e
		}
		in, e := os.Open(p)
		if e != nil {
			return e
		}
		h := newHasher()
		n, e := io.Copy(h, io.LimitReader(in, f.Size+1))
		in.Close()
		if e != nil || n != f.Size || fmt.Sprintf("%x", h.Sum(nil)) != f.Hash {
			return errors.New("installed file verification failed")
		}
	}
	return nil
}
func seedMacStore(seed, root string) error {
	if _, e := os.Stat(filepath.Join(root, "active.json")); e == nil {
		return nil
	}
	a, e := readActive(seed)
	if e != nil {
		return e
	}
	m, e := installed(seed, a.ID, platformID())
	if e != nil {
		return e
	}
	if e = os.MkdirAll(filepath.Join(root, "releases"), 0700); e != nil {
		return e
	}
	unlock, e := acquireApplyLock(root)
	if e != nil {
		return e
	}
	defer unlock()
	if _, e = os.Stat(filepath.Join(root, "active.json")); e == nil {
		return nil
	}
	temp, e := os.MkdirTemp(filepath.Join(root, "releases"), ".seed-")
	if e != nil {
		return e
	}
	defer os.RemoveAll(temp)
	for _, f := range m.Files {
		if e = copyVerified(filepath.Join(seed, "releases", a.ID, filepath.FromSlash(f.Path)), filepath.Join(temp, filepath.FromSlash(f.Path)), f); e != nil {
			return e
		}
	}
	raw, e := os.ReadFile(filepath.Join(seed, "releases", a.ID, "manifest.json"))
	if e != nil {
		return e
	}
	if e = os.WriteFile(filepath.Join(temp, "manifest.json"), raw, 0644); e != nil {
		return e
	}
	final := filepath.Join(root, "releases", a.ID)
	if _, e = os.Lstat(final); os.IsNotExist(e) {
		if e = os.Rename(temp, final); e != nil {
			return e
		}
	}
	if e = verifyReleaseFiles(root, m); e != nil {
		return e
	}
	return atomicJSON(filepath.Join(root, "active.json"), a)
}

// Installer-only activation of an already installed signed payload. Keep the
// previous pointer until signature, inventory and monotonic sequence pass.
func activateInstalled(root, id, platform string) error {
	if !hashOK(id) {
		return errors.New("invalid release identity")
	}
	unlock, e := acquireApplyLock(root)
	if e != nil {
		return e
	}
	defer unlock()
	m, e := installed(root, id, platform)
	if e != nil {
		return e
	}
	if e = verifyReleaseFiles(root, m); e != nil {
		return e
	}
	a, e := readActive(root)
	if os.IsNotExist(e) {
		return atomicJSON(filepath.Join(root, "active.json"), Active{ID: id})
	}
	if e != nil {
		return e
	}
	if a.ID == id {
		return nil
	}
	old, e := installed(root, a.ID, platform)
	if e != nil {
		return e
	}
	if m.Sequence <= old.Sequence {
		return errors.New("installer downgrade rejected")
	}
	return atomicJSON(filepath.Join(root, "active.json"), Active{ID: id, Previous: a.ID})
}

// Reconstruct the signed package digest from staged files too: elevated apply
// never trusts a caller's earlier download validation.
func verifyStagedPackage(stage string, m Manifest) error {
	if m.Transport == nil {
		return nil
	}
	type location struct {
		Path   string
		Offset int64
	}
	locations := map[string]location{}
	for _, f := range m.Files {
		var offset int64
		for _, c := range f.Chunks {
			locations[c.Hash] = location{f.Path, offset}
			offset += c.Size
		}
	}
	whole := newHasher()
	for _, c := range m.Transport.Chunks {
		loc := locations[c.Hash]
		p := filepath.Join(stage, filepath.FromSlash(loc.Path))
		if e := regular(p); e != nil {
			return e
		}
		in, e := os.Open(p)
		if e != nil {
			return e
		}
		h := newHasher()
		n, e := io.Copy(io.MultiWriter(h, whole), io.NewSectionReader(in, loc.Offset, c.Size))
		in.Close()
		if e != nil || n != c.Size || fmt.Sprintf("%x", h.Sum(nil)) != c.Hash {
			return errors.New("staged package chunk rejected")
		}
	}
	if fmt.Sprintf("%x", whole.Sum(nil)) != m.Transport.Hash {
		return errors.New("staged package SHA256 rejected")
	}
	return nil
}
