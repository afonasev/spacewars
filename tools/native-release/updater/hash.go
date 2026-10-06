package main

import (
	"crypto/sha256"
	"hash"
)

func newHasher() hash.Hash { return sha256.New() }
