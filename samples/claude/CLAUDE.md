# Global rules

Personal defaults for every project. A project's own `CLAUDE.md` adds to these and wins on conflict.

## Working style

- Reply in English. Keep answers short and concrete.
- Ask before deleting files, dropping tables or rewriting git history.
- Never force-push to a shared branch.
- Prefer small commits with an imperative subject: `fix: handle an empty cart`.

## Code

- Formatting and naming: `rules/style.md`.
- Security basics apply everywhere: `rules/security.md`.
- Test conventions load when a test file is read: `~/.claude/rules/testing.md`.
- HTTP APIs follow `~/.claude/shared/api-conventions.md`.
- Database changes go through the `/db-migrate` skill, never by hand.

## Tools

The tools I use every day are listed in [TOOLS.md](TOOLS.md). Check it before suggesting a new one.

## Before you finish

1. Run the linter and the test suite.
2. Update the changelog if behavior changed.
3. Summarize what changed in three lines or fewer.

## Deployment

Release checklist: `~/.claude/rules/missing.md`.

## Memory

Each project keeps its own notes. Read the project's `MEMORY.md` first, then open only the notes that match the task.
