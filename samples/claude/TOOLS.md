# Tools

The tools I reach for first. Check this list before suggesting a new one.

| Tool | Use it for |
|---|---|
| `git` | version control: small commits, rebase before merge |
| `rg` (ripgrep) | searching code; faster than grep |
| `jq` | reading and reshaping JSON |
| `docker compose` | local databases and queues |
| `dotnet` | building and testing the C# services |
| `node` and `npm` | the web front ends |

## Not installed

- No graphical database clients. Use `psql` inside the database container.
- No global npm packages; run tools through `npx` so the version is pinned by the project.
