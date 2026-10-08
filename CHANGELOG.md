# Changelog

All notable changes to pusula are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Choose folder…** also finds folders of linked notes that have no `.obsidian/` folder, recognized by their content
  (an entry note, mostly Markdown files, wikilinks between the notes), and labels them "notes".
- The folder picker and the Sources page name the computer the server runs on, so a page opened from another device
  says whose folders it shows.
- When pusula listens on an address that other devices can reach, it says so in the terminal: there is no login.
- **Downloads.** Every release has one file per platform (Windows x64, Linux x64 and ARM64, macOS on Apple silicon)
  that carries its own .NET: download it and run it, nothing to install. Their SHA-256 sums are on the release page.
  The files are not signed yet; the README says what macOS and Windows ask before the first start.

### Changed

- The page and the default settings (port 5190 among them) are built into the program, so it works the same from
  whatever folder it is started in. A `wwwroot` folder where pusula is started is never served as its page.
- On Windows, **Choose folder…** also searches fixed and removable drives (system folders left out) and treats folders
  with the Hidden or System attribute, such as AppData, as hidden.
- Built and tested on Linux, Windows and macOS on every change.
- Warnings fail the build only in CI (`CI=true`): a newer .NET SDK or a new advisory for a package no longer stops
  `dotnet run` on your machine. `dotnet build -warnaserror` gives the same check locally.

### Fixed

- A folder written after the options on the command line (`--urls … ~/notes`) was silently ignored and the server
  showed the list of sources instead; pusula now stops with one line that says folders go before the options.
- Started without folders on a computer that has no `~/.claude` and no sources file, pusula stopped with a stack
  trace. It now opens with an empty list, and the **Sources** page asks for a folder.
- Where `~/.config` did not exist yet (on a new Mac, for one), no folder could be added on the **Sources** page: the
  sources file had no place. It now goes there anyway, and adding the first source creates the folder.
- A port that is already in use, often by a pusula that is still running, is reported in one line with exit code 1,
  and a start that fails no longer prints the host's stack trace before pusula's own line.

### Security

- Symbolic links that lead to a network location (a `\\server\share` path on Windows) are no longer followed when
  searching, listing, counting or indexing folders. Opening one makes Windows log on to that server with the user's
  credentials, and a link in a folder that someone else made, a cloned repository for one, could use that to collect
  a hash of them. Such a link is skipped, as an entry that cannot be read.

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
