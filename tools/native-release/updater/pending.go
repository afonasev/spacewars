package main

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
)

type Pending struct {
	Root         string `json:"root"`
	ID           string `json:"id"`
	Channel      string `json:"channel"`
	ManifestHash string `json:"manifest_sha256"`
}

func writePending(root, cache, stage string, m Manifest, raw []byte) error {
	if m.Transport == nil || m.Transport.Channel != channel {
		return errors.New("pending channel rejected")
	}
	return atomicJSON(filepath.Join(cache, "pending.json"), Pending{root, m.ID, channel, digest(raw)})
}
func readPending(root, cache string) (string, error) {
	var p Pending
	b, e := os.ReadFile(filepath.Join(cache, "pending.json"))
	if e != nil {
		return "", e
	}
	if e = json.Unmarshal(b, &p); e != nil {
		return "", e
	}
	if p.Root != root || p.Channel != channel || !hashOK(p.ID) || !hashOK(p.ManifestHash) {
		return "", errors.New("pending identity rejected")
	}
	stage := filepath.Join(cache, "staged", p.ID)
	raw, e := os.ReadFile(filepath.Join(stage, "manifest.json"))
	if e != nil {
		return "", e
	}
	m, e := verifyEnvelope(raw, platformID())
	if e != nil {
		return "", e
	}
	if m.ID != p.ID || digest(raw) != p.ManifestHash || m.Transport == nil || m.Transport.Channel != channel {
		return "", errors.New("pending manifest rejected")
	}
	a, e := readActive(root)
	if e != nil {
		return "", e
	}
	old, e := installed(root, a.ID, platformID())
	if e != nil {
		return "", e
	}
	if m.Sequence <= old.Sequence || !newerVersion(m.Version, old.Version) {
		_ = os.Remove(filepath.Join(cache, "pending.json"))
		return "", errors.New("stale pending")
	}
	return stage, nil
}
func rememberHighest(cache string, m Manifest) error {
	p := filepath.Join(cache, "highest.json")
	var old struct {
		Sequence int64  `json:"sequence"`
		Version  string `json:"version"`
		ID       string `json:"id"`
	}
	b, e := os.ReadFile(p)
	if e == nil {
		if e = json.Unmarshal(b, &old); e != nil {
			return e
		}
		if m.Sequence < old.Sequence || m.Sequence == old.Sequence && (m.Version != old.Version || m.ID != old.ID) || m.Sequence > old.Sequence && !newerVersion(m.Version, old.Version) {
			return errors.New("replayed update rejected")
		}
	} else if !os.IsNotExist(e) {
		return e
	}
	old.Sequence = m.Sequence
	old.Version = m.Version
	old.ID = m.ID
	return atomicJSON(p, old)
}
