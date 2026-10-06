package main

import (
	"crypto/ed25519"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"path"
	"strings"
)

var publicKey string // pinned at build time, never supplied by the update server
var origin = "https://spacewars.afonasev.tech/native/v1"

const protocol = 1
const chunkSize = 1024 * 1024

type Chunk struct {
	Hash string `json:"hash"`
	Size int64  `json:"size"`
}
type File struct {
	Path   string  `json:"path"`
	Size   int64   `json:"size"`
	Hash   string  `json:"hash"`
	Mode   uint32  `json:"mode"`
	Chunks []Chunk `json:"chunks"`
}
type Manifest struct {
	Schema    int        `json:"schema"`
	Sequence  int64      `json:"sequence"`
	Version   string     `json:"version"`
	Commit    string     `json:"commit"`
	ID        string     `json:"id"`
	Platform  string     `json:"platform"`
	Entry     string     `json:"entry"`
	Files     []File     `json:"files"`
	Transport *Transport `json:"transport,omitempty"`
}
type Envelope struct {
	Payload   string `json:"payload"`
	Signature string `json:"signature"`
}

func digest(b []byte) string { h := sha256.Sum256(b); return hex.EncodeToString(h[:]) }
func hashOK(s string) bool {
	b, e := hex.DecodeString(s)
	return e == nil && len(b) == 32 && s == strings.ToLower(s)
}
func safePath(s string) bool {
	if s == "" || len(s) > 240 || strings.ContainsAny(s, "\\:\x00") || strings.HasPrefix(s, "/") || path.Clean(s) != s {
		return false
	}
	for _, p := range strings.Split(s, "/") {
		if p == "." || p == ".." || strings.HasSuffix(p, ".") || strings.HasSuffix(p, " ") {
			return false
		}
		head := strings.ToUpper(strings.Split(p, ".")[0])
		if head == "CON" || head == "PRN" || head == "AUX" || head == "NUL" || strings.HasPrefix(head, "COM") && len(head) == 4 || strings.HasPrefix(head, "LPT") && len(head) == 4 {
			return false
		}
	}
	return true
}
func verifyEnvelope(raw []byte, platform string) (Manifest, error) {
	var m Manifest
	var e Envelope
	if len(raw) > 16*1024*1024 {
		return m, errors.New("manifest too large")
	}
	if err := json.Unmarshal(raw, &e); err != nil {
		return m, err
	}
	key, err := base64.StdEncoding.DecodeString(publicKey)
	if err != nil || len(key) != ed25519.PublicKeySize {
		return m, errors.New("invalid pinned key")
	}
	payload, err := base64.StdEncoding.DecodeString(e.Payload)
	if err != nil {
		return m, err
	}
	sig, err := base64.StdEncoding.DecodeString(e.Signature)
	if err != nil || !ed25519.Verify(key, payload, sig) {
		return m, errors.New("release signature rejected")
	}
	if err = json.Unmarshal(payload, &m); err != nil {
		return m, err
	}
	if m.Schema != protocol || m.Sequence < 1 || m.Platform != platform || !hashOK(m.ID) || !safePath(m.Entry) || len(m.Files) == 0 || len(m.Files) > 20000 || m.Version == "" {
		return m, errors.New("incompatible release manifest")
	}
	seen := map[string]bool{}
	entry := false
	for _, f := range m.Files {
		low := strings.ToLower(f.Path)
		if !safePath(f.Path) || seen[low] || f.Size < 0 || !hashOK(f.Hash) || f.Mode != 0644 && f.Mode != 0755 {
			return m, fmt.Errorf("invalid release file: %s", f.Path)
		}
		seen[low] = true
		if f.Path == m.Entry {
			entry = true
		}
		var n int64
		for _, c := range f.Chunks {
			if !hashOK(c.Hash) || c.Size <= 0 || c.Size > chunkSize {
				return m, errors.New("invalid chunk")
			}
			n += c.Size
		}
		if n != f.Size {
			return m, errors.New("chunk sizes do not match file")
		}
	}
	if !entry {
		return m, errors.New("missing entrypoint")
	}
	for p := range seen {
		for q := path.Dir(p); q != "."; q = path.Dir(q) {
			if seen[q] {
				return m, errors.New("file/directory collision")
			}
		}
	}
	if _, e := versionParts(m.Version); e != nil {
		return m, e
	}
	if e := validateTransport(m); e != nil {
		return m, e
	}
	return m, nil
}
