package main

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
)

// The installer extracts to its own temporary directory. Only this verified,
// locked transaction may change the installation; a running host denies it.
func installPayload(root, payload, id string) (result error) {
	if !hashOK(id) {
		return errors.New("invalid installer release")
	}
	if err := os.MkdirAll(root, 0755); err != nil {
		return err
	}
	base, err := os.UserCacheDir()
	if err != nil {
		return err
	}
	cache := filepath.Join(base, "Spacewars", storeNamespace(), digest([]byte(root)))
	if err = os.MkdirAll(cache, 0700); err != nil {
		return err
	}
	unlock, err := acquireHostLock(root, cache)
	if err != nil {
		return fmt.Errorf("close the running game and retry: %w", err)
	}
	defer unlock()
	unlockApply, err := acquireApplyLock(root)
	if err != nil {
		return err
	}
	defer unlockApply()
	m, err := installed(payload, id, platformID())
	if err != nil {
		return err
	}
	if err = verifyReleaseFiles(payload, m); err != nil {
		return err
	}
	if m.Transport != nil && m.Transport.Channel != channel {
		return errors.New("installer channel mismatch")
	}
	previous, readErr := readActive(root)
	if readErr == nil {
		old, e := installed(root, previous.ID, platformID())
		if e != nil {
			return e
		}
		if previous.ID != id && (m.Sequence <= old.Sequence || !newerVersion(m.Version, old.Version)) {
			return errors.New("installer downgrade rejected")
		}
	} else if !os.IsNotExist(readErr) {
		return readErr
	}
	if err = requireSpace(root, requiredStageSpace(m)); err != nil {
		return err
	}
	releases := filepath.Join(root, "releases")
	if err = os.MkdirAll(releases, 0755); err != nil {
		return err
	}
	temp, err := os.MkdirTemp(releases, ".install-")
	if err != nil {
		return err
	}
	defer os.RemoveAll(temp)
	for _, f := range m.Files {
		if err = copyVerified(filepath.Join(payload, "releases", id, filepath.FromSlash(f.Path)), filepath.Join(temp, filepath.FromSlash(f.Path)), f); err != nil {
			return err
		}
	}
	raw, err := os.ReadFile(filepath.Join(payload, "releases", id, "manifest.json"))
	if err != nil {
		return err
	}
	if err = os.WriteFile(filepath.Join(temp, "manifest.json"), raw, 0644); err != nil {
		return err
	}
	// Bootstrap and UpdateHost are the same signed helper bytes.
	var helper *File
	for i := range m.Files {
		if m.Files[i].Path == executableName("UpdateHost") {
			helper = &m.Files[i]
			break
		}
	}
	if helper == nil {
		return errors.New("signed update helper missing")
	}
	bootstrap := filepath.Join(root, executableName("Spacewars"))
	incoming := filepath.Join(root, ".bootstrap-new")
	if err = copyVerified(filepath.Join(payload, executableName("Spacewars")), incoming, *helper); err != nil {
		return err
	}
	defer os.Remove(incoming)
	final := filepath.Join(releases, id)
	backup := temp + "-previous"
	hadFinal := false
	if _, err = os.Lstat(final); err == nil {
		if err = os.Rename(final, backup); err != nil {
			return err
		}
		hadFinal = true
	}
	if err = os.Rename(temp, final); err != nil {
		if hadFinal {
			_ = os.Rename(backup, final)
		}
		return err
	}
	bootstrapBackup := filepath.Join(root, ".bootstrap-previous")
	hadBootstrap := false
	if _, err = os.Lstat(bootstrap); err == nil {
		if err = os.Rename(bootstrap, bootstrapBackup); err != nil {
			result = err
		} else {
			hadBootstrap = true
		}
	}
	if result == nil {
		result = replaceFile(incoming, bootstrap)
	}
	next := Active{ID: id}
	if readErr == nil {
		if previous.ID == id {
			next.Previous = previous.Previous
		} else {
			next.Previous = previous.ID
		}
	}
	if result == nil {
		result = atomicJSON(filepath.Join(root, "active.json"), next)
	}
	if result != nil {
		if hadBootstrap {
			_ = os.Remove(bootstrap)
			_ = os.Rename(bootstrapBackup, bootstrap)
		}
		_ = os.RemoveAll(final)
		if hadFinal {
			_ = os.Rename(backup, final)
		}
		return result
	}
	if hadBootstrap {
		_ = os.Remove(bootstrapBackup)
	}
	if hadFinal {
		_ = os.RemoveAll(backup)
	}
	// The icon is presentation metadata from this explicitly launched installer.
	if icon, e := os.ReadFile(filepath.Join(payload, "Spacewars.ico")); e == nil {
		_ = os.WriteFile(filepath.Join(root, "Spacewars.ico"), icon, 0644)
	}
	return nil
}
func requiredStageSpace(m Manifest) uint64 {
	var size uint64
	for _, f := range m.Files {
		size += uint64(f.Size)
	}
	return size*2 + 16*1024*1024
}
func requireSpace(path string, needed uint64) error {
	available, err := freeBytes(path)
	if err != nil {
		return err
	}
	if available < needed {
		return fmt.Errorf("not enough disk space: need %d bytes, available %d; free space and retry", needed, available)
	}
	return nil
}
