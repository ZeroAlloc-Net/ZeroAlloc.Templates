# ZeroAlloc.Templates

`dotnet new` templates for the ZeroAlloc.* ecosystem. Currently ships one template: `za-clean` — a Clean Architecture Web API wired with 10 ZA.* packages.

## Quickstart

```bash
dotnet new install ZeroAlloc.Templates
dotnet new za-clean -o MyApp
cd MyApp
dotnet run --project src/MyApp.Api
```

See [docs/za-clean.md](docs/za-clean.md) for the full tour.

## Required GitHub repo secrets (for maintainers)

The release pipeline requires two secrets at `Settings → Secrets and variables → Actions`:

- `RELEASE_PLEASE_TOKEN` — Personal Access Token with `contents:write` + `pull-requests:write` + `workflow:write` scopes. Needed because the default `GITHUB_TOKEN` cannot trigger downstream workflows from release-please-created PRs.
- `NUGET_API_KEY` — Push-only API key from nuget.org, scoped to `ZeroAlloc.Templates`.

## Development

Several config files (`Directory.Build.props`, `Directory.Packages.props`, `global.json`) are duplicated at the repo root and under `content/za-clean/`. CI enforces they stay in sync — update both copies when bumping package versions or SDK pins.

Build + test:

```bash
dotnet build content/za-clean/MyApp.slnx
dotnet test content/za-clean/MyApp.slnx
dotnet pack ZeroAlloc.Templates.csproj -o ./nupkg
dotnet test tests/ZeroAlloc.Templates.SmokeTests
```

## License

MIT — see [LICENSE](LICENSE).
