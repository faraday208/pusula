# pusula

A local, read-only web tool that shows AI agent configuration folders (`~/.claude`), Obsidian vaults and plain
Markdown folders in the browser: file tree, rendered Markdown, links and backlinks, when each file is loaded and
what it costs in tokens, and broken, orphan and not-yet-written links.

## Commands

Run them from this folder: `global.json` here switches `dotnet test` to Microsoft.Testing.Platform.

```bash
dotnet build pusula.slnx                                   # warnings are errors
dotnet test --solution pusula.slnx                         # unit + integration tests
node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test "tests/web/*.test.mjs"   # UI tests
dotnet run --project src/Pusula                            # sources from ~/.config/pusula/sources.json, http://localhost:5190
dotnet run --project src/Pusula -- ~/.claude ~/notes       # only these folders
dotnet run --project src/Pusula -- "$PWD/samples/claude" "$PWD/samples/vault"   # synthetic demo
dotnet run --project src/Pusula -- --version                # prints the version (also on the Sources page)
dotnet run --project src/Pusula -- --urls http://<private-network-IP>:5190 --Pusula:AllowRemoteEdit true
```

## Layout

- `src/Pusula`: one ASP.NET Core project (.NET 10, minimal APIs) in vertical slices. Dependencies point one way:
  `Sources/ Browse/ Tree/ Files/ Overview/ LiveReload/ → Indexing/ → Links/`. `Security/` holds the host guard and
  the security headers. There is no database: the file system is the source of truth, the index is an in-memory snapshot.
- `src/Pusula/wwwroot`: the UI, plain HTML/CSS/JS as ES modules with no build step; `markdown-it` is vendored.
- `tests/Pusula.UnitTests`, `tests/Pusula.IntegrationTests` (xUnit v3, Shouldly), `tests/web` (node:test).
- `samples/`: a synthetic Claude configuration and a small vault for trying pusula and for the README screenshots
  (`docs/screenshots/`). Folders on the command line must be absolute: `dotnet run` starts in `src/Pusula`.
- API contract: `/openapi/v1.json`; example requests: `src/Pusula/Pusula.http`.

## Rules

- **Read-only.** Never write to a folder that is being shown. The only file pusula writes is its own `sources.json`.
- **The UI talks to the server only through the JSON API.** The server never renders HTML; the UI never sees the file system.
- **Only `.md` files are indexed and served.** `/api/sources/{id}/file` looks paths up in the index, never on disk.
- **Changing sources and browsing folders** is allowed from the same machine only (loopback or the server's own
  address; requests with forwarded headers count as remote) unless `Pusula:AllowRemoteEdit` is true. The
  `Origin` / `Sec-Fetch-Site` checks and the host guard always apply. pusula has no login: never expose it to the internet.
- **Test data is synthetic.** No real configuration content, people, project names, host names or IP addresses
  (use 192.0.2.x, 198.51.100.x, 203.0.113.x).
- **Two languages.** Every UI string exists in Turkish and English (`wwwroot/js/i18n.js`). Text contrast ≥ 4.5:1,
  touch targets ≥ 40px.
- **Small core.** Every feature rests on a real use; unused features are removed.
