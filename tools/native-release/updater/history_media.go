package main

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"image/png"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"
)

const historyImageLimit = 8 * 1024 * 1024

var historyImageName = regexp.MustCompile(`^notes-([a-f0-9]{64})\.png$`)
var markdownImage = regexp.MustCompile(`^!\[([^\]\r\n]*)\]\(([^\s)]+)\)$`)

type HistoryBlock struct {
	Kind  string `json:"kind"`
	Text  string `json:"text"`
	Asset string `json:"asset,omitempty"`
}

func historyBlocks(text string, r githubRelease) []HistoryBlock {
	result := []HistoryBlock{}
	for _, line := range strings.Split(text, "\n") {
		line = strings.TrimSpace(line)
		if line == "" {
			continue
		}
		if m := markdownImage.FindStringSubmatch(line); m != nil {
			asset := ""
			for _, a := range r.Assets {
				expected := "https://github.com/" + repository + "/releases/download/" + r.Tag + "/" + a.Name
				if historyImageName.MatchString(a.Name) && a.URL == expected && m[2] == expected && a.Size > 0 && a.Size <= historyImageLimit {
					asset = a.Name
					break
				}
			}
			if asset != "" {
				result = append(result, HistoryBlock{Kind: "image", Text: m[1], Asset: asset})
			} else {
				result = append(result, HistoryBlock{Kind: "text", Text: m[1] + " · Изображение недоступно"})
			}
		} else if strings.HasPrefix(line, "### ") {
			result = append(result, HistoryBlock{Kind: "heading", Text: strings.TrimPrefix(line, "### ")})
		} else if strings.HasPrefix(line, "- ") {
			result = append(result, HistoryBlock{Kind: "item", Text: strings.TrimPrefix(line, "- ")})
		} else {
			result = append(result, HistoryBlock{Kind: "text", Text: line})
		}
	}
	return result
}
func historyDirectory(root string) string { return filepath.Join(root, "history", channel) }
func readHistoryCache(root, installed string) (ReleaseHistory, error) {
	var h ReleaseHistory
	b, e := os.ReadFile(filepath.Join(historyDirectory(root), "history.json"))
	if e != nil {
		return h, e
	}
	if len(b) > 16*1024*1024 {
		return h, errors.New("history cache too large")
	}
	if e = json.Unmarshal(b, &h); e != nil {
		return h, e
	}
	for _, r := range h.Releases {
		if r.Version == installed {
			return h, nil
		}
	}
	return h, errors.New("installed release absent from cache")
}
func cachedHistory(ctx context.Context, root, installed string) (ReleaseHistory, error) {
	fetchContext, cancel := context.WithTimeout(ctx, 8*time.Second)
	defer cancel()
	h, e := githubHistoryContext(fetchContext, installed)
	if e != nil {
		if ctx.Err() != nil {
			return h, ctx.Err()
		}
		return readHistoryCache(root, installed)
	}
	dir := historyDirectory(root)
	if os.MkdirAll(dir, 0700) == nil {
		_ = atomicJSON(filepath.Join(dir, "history.json"), h)
	}
	return h, nil
}
func validateHistoryImage(b []byte, name string) error {
	m := historyImageName.FindStringSubmatch(name)
	if m == nil || len(b) > historyImageLimit {
		return errors.New("invalid history image")
	}
	digest := sha256.Sum256(b)
	if hex.EncodeToString(digest[:]) != m[1] {
		return errors.New("history image hash mismatch")
	}
	c, e := png.DecodeConfig(bytes.NewReader(b))
	if e != nil || c.Width < 1 || c.Height < 1 || c.Width > 4096 || c.Height > 4096 {
		return errors.New("invalid image dimensions/format")
	}
	return nil
}
func historyImage(ctx context.Context, root, installed, version, name string) ([]byte, error) {
	if _, e := versionParts(version); e != nil {
		return nil, e
	}
	if !historyImageName.MatchString(name) {
		return nil, errors.New("invalid media name")
	}
	h, e := readHistoryCache(root, installed)
	if e != nil {
		return nil, e
	}
	allowed := false
	for _, r := range h.Releases {
		if r.Version == version {
			for _, b := range append(append([]HistoryBlock{}, r.BlocksRu...), r.BlocksEn...) {
				if b.Kind == "image" && b.Asset == name {
					allowed = true
				}
			}
		}
	}
	if !allowed {
		return nil, errors.New("image is not an attachment of this release")
	}
	dir := filepath.Join(historyDirectory(root), "media")
	path := filepath.Join(dir, name)
	if b, e := os.ReadFile(path); e == nil && validateHistoryImage(b, name) == nil {
		return b, nil
	}
	b, e := githubFetchContext(ctx, fmt.Sprintf("https://github.com/%s/releases/download/v%s/%s", repository, version, name), historyImageLimit)
	if e != nil {
		return nil, e
	}
	if e = validateHistoryImage(b, name); e != nil {
		return nil, e
	}
	if ctx.Err() != nil {
		return nil, ctx.Err()
	}
	if os.MkdirAll(dir, 0700) == nil {
		temp, e := os.CreateTemp(dir, ".image-*")
		if e == nil {
			tmp := temp.Name()
			_, e = temp.Write(b)
			closeErr := temp.Close()
			if e == nil && closeErr == nil {
				_ = replaceFile(tmp, path)
			}
			_ = os.Remove(tmp)
		}
	}
	return b, nil
}
