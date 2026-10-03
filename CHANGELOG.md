# Changelog

All notable changes to pusula are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-10-03

The first public release. Runs from source with the .NET 10 SDK (`dotnet run`).

### Added

- **Reading view:** file tree, rendered Markdown (wikilinks, callouts, task lists, tags, embedded notes), links and
  backlinks with their line numbers, quick open (Ctrl+K, Ctrl+O or `/`), reading position kept on Back and reload.
- **Claude Code profile** for `~/.claude`: when each file is loaded (every session, path-scoped rules, descriptions
  of skills, agents and commands, on demand, user-invoked, inactive output styles), estimated tokens per file and per
  session, memory links resolved by frontmatter `name`, project memory indexes.
- **Obsidian vault and Markdown profiles:** Obsidian-style link resolution, tags from frontmatter and from the text,
  the entry note, recently changed and most linked notes.
- **Health checks:** broken links, orphan files, links to notes that are not written yet, frontmatter errors with
  the line they are on.
- **Sources:** a list in `sources.json` that is watched for changes, folders on the command line, and adding or
  removing sources from the browser with a folder picker — from the same machine only, or from a private network
  with `--Pusula:AllowRemoteEdit true`.
- **Live reload** over server-sent events.
- **Safety:** read-only access to the folders shown, only `.md` files served, a host guard against DNS rebinding,
  a strict Content Security Policy, origin checks for every change, scan limits for very large folders.
- English and Turkish user interface; keyboard and touch friendly.
- A synthetic demo in `samples/`; the version is shown on the Sources page and printed by `--version`.

[Unreleased]: https://github.com/faraday208/pusula/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/faraday208/pusula/releases/tag/v0.1.0
