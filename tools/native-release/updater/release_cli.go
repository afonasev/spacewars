package main

import (
	"crypto/ed25519"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
)

func signDocument(input, keyPath, output string) error {
	b, e := os.ReadFile(input)
	if e != nil {
		return e
	}
	kb, e := os.ReadFile(keyPath)
	if e != nil {
		return e
	}
	key, e := base64.StdEncoding.DecodeString(string(kb))
	if e != nil || len(key) != ed25519.PrivateKeySize {
		return errors.New("bad signing key")
	}
	if base64.StdEncoding.EncodeToString(key[32:]) != publicKey {
		return errors.New("signing key differs from trust")
	}
	raw, e := json.Marshal(Envelope{base64.StdEncoding.EncodeToString(b), base64.StdEncoding.EncodeToString(ed25519.Sign(key, b))})
	if e != nil {
		return e
	}
	return os.WriteFile(output, raw, 0644)
}
func verifyPackageFile(rawPath, platform, packagePath string) error {
	raw, e := os.ReadFile(rawPath)
	if e != nil {
		return e
	}
	m, e := verifyEnvelope(raw, platform)
	if e != nil {
		return e
	}
	if m.Transport == nil {
		return errors.New("missing signed transport")
	}
	f, e := os.Open(packagePath)
	if e != nil {
		return e
	}
	defer f.Close()
	st, e := f.Stat()
	if e != nil || st.Size() != m.Transport.Size {
		return errors.New("package size rejected")
	}
	whole := newHasher()
	for _, c := range m.Transport.Chunks {
		h := newHasher()
		n, e := io.Copy(io.MultiWriter(h, whole), io.LimitReader(f, c.Size))
		if e != nil || n != c.Size || fmt.Sprintf("%x", h.Sum(nil)) != c.Hash {
			return errors.New("package chunk hash rejected")
		}
	}
	if fmt.Sprintf("%x", whole.Sum(nil)) != m.Transport.Hash {
		return errors.New("package SHA256 rejected")
	}
	fmt.Printf("{\"platform\":%q,\"version\":%q,\"channel\":%q,\"size\":%d,\"sha256\":%q,\"signature\":\"verified\"}\n", m.Platform, m.Version, m.Transport.Channel, m.Transport.Size, m.Transport.Hash)
	return nil
}
