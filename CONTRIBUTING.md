# Contributing to ZeroAlloc.Templates

For maintainers of this repo, not for adopters of the templates.

## Required GitHub repo secrets

The release pipeline requires two secrets at `Settings → Secrets and variables → Actions`. Both are configured at org level so they inherit:

- `RELEASE_PLEASE_TOKEN` — Personal Access Token with `contents:write` + `pull-requests:write` + `workflow:write`. Needed because the default `GITHUB_TOKEN` can't trigger downstream workflows from release-please PRs.
- `NUGET_API_KEY` — push-only API key from nuget.org, scoped to `ZeroAlloc.Templates`.

## Drift-guard for duplicated config

Three files are duplicated at the repo root and under each `content/<template>/`:

- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`

The root copies serve the template-tooling projects; the `content/` copies serve the scaffolded app standalone. **CI enforces they stay identical** across the root and every `content/<template>/` directory via a drift-guard step. When bumping a package version or SDK pin, edit all copies.

## Build + test

```bash
# Per-template (replace za-clean ↔ za-vertical-slice as needed):
dotnet build content/za-clean/MyApp.slnx
dotnet test content/za-clean/MyApp.slnx
dotnet pack ZeroAlloc.Templates.csproj -o ./nupkg

# Full smoke test (parameterized over both templates: install + scaffold + build + test, ~50 s):
dotnet test tests/ZeroAlloc.Templates.SmokeTests

# Skip the slow smoke tests during inner-loop dev:
dotnet test --filter "Category!=Slow"
```

See [AGENTS.md](AGENTS.md) for the AI-agent orientation.
