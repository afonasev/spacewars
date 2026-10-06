package main

import (
	"crypto/ed25519"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strconv"
)

func keygen(name string) error {
	pub, priv, e := ed25519.GenerateKey(rand.Reader)
	if e != nil {
		return e
	}
	if e = os.MkdirAll(filepath.Dir(name), 0700); e != nil {
		return e
	}
	f, e := os.OpenFile(name, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if e != nil {
		return e
	}
	_, e = f.Write([]byte(base64.StdEncoding.EncodeToString(priv)))
	f.Close()
	if e != nil {
		return e
	}
	fmt.Println(base64.StdEncoding.EncodeToString(pub))
	return nil
}
func pack(args []string) error {
	if len(args) != 8 && len(args) != 9 {
		return errors.New("pack INPUT OUTPUT PRIVATE_KEY PLATFORM ENTRY VERSION SEQUENCE COMMIT")
	}
	input, out, keyFile, platform, entry, version, seq, commit := args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]
	kb, e := os.ReadFile(keyFile)
	if e != nil {
		return e
	}
	key, e := base64.StdEncoding.DecodeString(string(kb))
	if e != nil || len(key) != ed25519.PrivateKeySize {
		return errors.New("bad signing key")
	}
	sequence, e := strconv.ParseInt(seq, 10, 64)
	if e != nil {
		return e
	}
	m := Manifest{Schema: 1, Sequence: sequence, Version: version, Platform: platform, Entry: entry, Commit: commit}
	objects := filepath.Join(out, "objects")
	if e = os.MkdirAll(objects, 0755); e != nil {
		return e
	}
	e = filepath.WalkDir(input, func(p string, d os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if d.IsDir() {
			return nil
		}
		if d.Type()&os.ModeSymlink != 0 {
			return fmt.Errorf("release symlink not allowed: %s", p)
		}
		st, e := d.Info()
		if e != nil {
			return e
		}
		rel, e := filepath.Rel(input, p)
		if e != nil {
			return e
		}
		rel = filepath.ToSlash(rel)
		if !safePath(rel) {
			return errors.New("unsafe release filename")
		}
		in, e := os.Open(p)
		if e != nil {
			return e
		}
		defer in.Close()
		f := File{Path: rel, Size: st.Size(), Mode: 0644}
		if st.Mode()&0111 != 0 {
			f.Mode = 0755
		}
		h := sha256.New()
		buffer := make([]byte, chunkSize)
		for {
			n, err := io.ReadFull(in, buffer)
			if n > 0 {
				b := buffer[:n]
				hash := digest(b)
				f.Chunks = append(f.Chunks, Chunk{Hash: hash, Size: int64(n)})
				h.Write(b)
				target := filepath.Join(objects, hash)
				if _, e := os.Stat(target); os.IsNotExist(e) {
					if e = os.WriteFile(target, b, 0644); e != nil {
						return e
					}
				}
			}
			if err == io.EOF || err == io.ErrUnexpectedEOF {
				break
			}
			if err != nil {
				return err
			}
		}
		f.Hash = fmt.Sprintf("%x", h.Sum(nil))
		m.Files = append(m.Files, f)
		return nil
	})
	if e != nil {
		return e
	}
	sort.Slice(m.Files, func(i, j int) bool { return m.Files[i].Path < m.Files[j].Path })
	if len(args) == 9 {
		ch := args[8]
		if ch != "test" && ch != "production" {
			return errors.New("invalid channel")
		}
		t := &Transport{Channel: ch, Tag: "v" + version, Asset: "Spacewars-" + version + "-" + platform + ".pack"}
		dir := filepath.Join(out, "github")
		if e = os.MkdirAll(dir, 0755); e != nil {
			return e
		}
		stream, e := os.Create(filepath.Join(dir, t.Asset))
		if e != nil {
			return e
		}
		h := newHasher()
		seen := map[string]bool{}
		for _, f := range m.Files {
			for _, c := range f.Chunks {
				if seen[c.Hash] {
					continue
				}
				seen[c.Hash] = true
				b, e := os.ReadFile(filepath.Join(objects, c.Hash))
				if e != nil {
					stream.Close()
					return e
				}
				if _, e = stream.Write(b); e != nil {
					stream.Close()
					return e
				}
				h.Write(b)
				t.Chunks = append(t.Chunks, PackageChunk{Hash: c.Hash, Size: c.Size, Offset: t.Size})
				t.Size += c.Size
			}
		}
		if e = stream.Close(); e != nil {
			return e
		}
		t.Hash = fmt.Sprintf("%x", h.Sum(nil))
		m.Transport = t
	}
	identity, _ := json.Marshal(m)
	m.ID = digest(identity)
	payload, e := json.Marshal(m)
	if e != nil {
		return e
	}
	envelope := Envelope{Payload: base64.StdEncoding.EncodeToString(payload), Signature: base64.StdEncoding.EncodeToString(ed25519.Sign(key, payload))}
	raw, e := json.Marshal(envelope)
	if e != nil {
		return e
	}
	old := publicKey
	publicKey = base64.StdEncoding.EncodeToString(key[32:])
	_, e = verifyEnvelope(raw, platform)
	publicKey = old
	if e != nil {
		return e
	}
	dir := filepath.Join(out, platform)
	if e = os.MkdirAll(dir, 0755); e != nil {
		return e
	}
	if e = os.WriteFile(filepath.Join(dir, "latest.json"), raw, 0644); e != nil {
		return e
	}
	if m.Transport != nil {
		name := "Spacewars-" + version + "-" + platform + ".json"
		if e = os.WriteFile(filepath.Join(out, "github", name), raw, 0644); e != nil {
			return e
		}
		bridge := m
		bridge.Transport = nil
		bp, _ := json.Marshal(bridge)
		br, _ := json.Marshal(Envelope{Payload: base64.StdEncoding.EncodeToString(bp), Signature: base64.StdEncoding.EncodeToString(ed25519.Sign(key, bp))})
		if e = os.WriteFile(filepath.Join(dir, "bridge.json"), br, 0644); e != nil {
			return e
		}
	}
	fmt.Println(m.ID)
	return nil
}
