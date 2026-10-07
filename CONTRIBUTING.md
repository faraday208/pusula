# Contributing to pusula

Thank you for wanting to help. pusula is a small, selective project with one maintainer: every feature rests on a
real use, and a feature nobody uses is removed. Bug reports, fixes and ideas are welcome. By taking part you agree to
follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

- **A small fix** (a typo, a clear bug with a test): send a pull request.
- **Anything bigger** (a new feature, a change in behavior, a new dependency): please **open an issue first**, so we
  can agree on it before you spend time on it.
- **A security problem:** do **not** open an issue. Report it privately, as described in [SECURITY.md](SECURITY.md).

## Build and test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, for the UI tests, [Node.js](https://nodejs.org) 22
(the version CI uses; 22.7 or newer). The UI tests have no npm dependencies, so there is nothing to install.

Run these from the repository root; `global.json` there switches `dotnet test` to Microsoft.Testing.Platform:

```bash
dotnet build pusula.slnx -warnaserror                                               # warnings are errors, as in CI
dotnet test --solution pusula.slnx                                                  # unit and integration tests
node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test "tests/web/*.test.mjs"   # UI tests
```

CI runs the same three commands on every push to `main` and on every pull request. Warnings fail the build only where
`CI=true` (GitHub Actions sets it), so that a newer SDK or a new package advisory never stops someone who only runs
pusula; `-warnaserror` above gives the same check locally.

## Try your change

Start pusula on the synthetic demo in `samples/`, then open <http://localhost:5190>:

```bash
dotnet run --project src/Pusula -- "$PWD/samples/claude" "$PWD/samples/vault"
```

Give folders as absolute paths: `dotnet run` starts the server in `src/Pusula`, so a relative path is looked up there.
`"$PWD/…"` works in bash, zsh and PowerShell.

## Rules

Please keep to these. [CLAUDE.md](CLAUDE.md) has the same rules in more detail, with the layout of the code.

- **Read-only.** pusula never writes to a folder it shows. The only file it writes is its own `sources.json`.
- **Only `.md` files** are indexed and served.
- **The UI talks to the server only through the JSON API.** The server never renders HTML; the UI never sees the file system.
- **Test data is synthetic.** No real configuration content, people, project names, host names or IP addresses.
  For addresses use the documentation ranges 192.0.2.x, 198.51.100.x and 203.0.113.x.
- **Two languages.** Every UI string exists in Turkish and English (`src/Pusula/wwwroot/js/i18n.js`).
- **Small core.** A feature needs a real use. Do not add one "just in case".

## Pull requests

- The three commands above pass, and CI is green.
- New behavior comes with a test; a bug fix comes with a test that fails without the fix.
- Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md), in the group that fits (Added, Changed, Fixed, ...).
- Keep the change to one concern. Please do not reformat or rename code that the change does not need to touch.

Start each commit message with a type: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `perf:`, `style:`, `chore:` or
`security:`. The description can be in English or in Turkish, for example `docs: fix a typo in the Remote access section`.

By sending a contribution you agree that it is licensed under the [Apache License 2.0](LICENSE), as section 5 of the
license says.
