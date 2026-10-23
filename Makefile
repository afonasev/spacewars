
.PHONY: storage-check
storage-check: ## Check compact documentation and evidence storage.
	python3 tools/worktree_storage.py check
