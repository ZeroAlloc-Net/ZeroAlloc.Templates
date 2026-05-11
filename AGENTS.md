# AGENTS.md — ZeroAlloc.Templates

> For AI coding agents working on the `ZeroAlloc.Templates` repo itself (not on a scaffolded app).

For agents working on a scaffolded `dotnet new za-clean` output, see [content/za-clean/AGENTS.md](content/za-clean/AGENTS.md).

## 1. Repo split

This repo has two distinct surfaces:

| Surface | Path | What lives there |
|---|---|---|
| **Template payload** | `content/za-clean/` | The actual scaffolded app — `src/`, `tests/`, `benchmarks/`, `MyApp.slnx`, plus its own `Directory.Build.props` / `Directory.Packages.props` / `global.json`. **Everything under here ships in the NuGet template package.** |
| **Template tooling** | Repo root | `ZeroAlloc.Templates.csproj` (the NuGet template package), `tests/ZeroAlloc.Templates.SmokeTests/` (smoke gate), `.github/workflows/` (CI + release-please), `docs/`, `README.md`. **Does not ship in the template package.** |

The template engine's `sourceName: MyApp` substitution applies to file contents and filenames under `content/za-clean/`. The repo root is unaffected.

## 2. Drift-guard for duplicated config

Three files are duplicated between the repo root and `content/za-clean/`:

- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`

The repo-root copies serve the template-tooling projects (smoke test + template package). The `content/za-clean/` copies serve the scaffolded app (so it works standalone after `dotnet new za-clean`).

**CI enforces they stay identical** via the drift-guard step in `.github/workflows/ci.yml`. When bumping a package version or SDK pin, **edit both copies**. CI will refuse to merge if they differ.

## 3. Verify before commit

Full pipeline:

```bash
# 1. Content builds + tests pass
dotnet build content/za-clean/MyApp.slnx
dotnet test content/za-clean/MyApp.slnx

# 2. Template package builds
dotnet pack ZeroAlloc.Templates.csproj -o ./nupkg

# 3. Smoke test (install + scaffold + build + test, ~25 s)
dotnet test tests/ZeroAlloc.Templates.SmokeTests

# Skip the slow smoke test during inner-loop:
dotnet test --filter "Category!=Slow"
```

Pre-PR checklist:
- All four steps above are green
- If you touched the content, the smoke test confirms it still scaffolds + builds
- Conventional commits (`feat(application): …`, `fix(infrastructure): …`, `docs(template): …`)
- Drift-guard passes (root vs content/ config files identical)
