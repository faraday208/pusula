---
name: release-notes
description: Draft release notes from the commits since the last tag. Use when the user asks for a changelog, release notes or "what changed since v1.2".
---
# Release notes

1. Find the last tag with `git describe --tags --abbrev=0`.
2. List the commits since it with `git log <tag>..HEAD --oneline`.
3. Group them by type (feat, fix, perf, docs) and drop the chores.
4. Fill in the layout from [template.md](template.md).
5. Keep each entry to one line and mention the issue number when the commit has one.
