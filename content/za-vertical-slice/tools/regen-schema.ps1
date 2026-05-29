# Regenerate both providers' migration histories + embedded schema scripts
# after entity changes. Run from the za-vertical-slice root.
#
# Usage: .\tools\regen-schema.ps1 [-Name MigrationName]
#   -Name defaults to "Update".
param([string]$Name = "Update")

$ErrorActionPreference = "Stop"

Push-Location "$PSScriptRoot/../src/MyApp"
try {
    # Provider must be set as an env var (not -p:...) because `dotnet ef` aliases
    # -p to --project. The MyApp.csproj's MSBuild conditionals on
    # $(EfActiveProvider) exclude the wrong provider's migration classes from
    # compilation, so `dotnet ef migrations add/script` sees only the active
    # provider's [Migration] classes.

    $env:EfActiveProvider = "Sqlite"
    dotnet ef migrations add $Name --context AppDbContext `
      --output-dir Persistence/Migrations.Sqlite --namespace MyApp.Persistence.Migrations.Sqlite -- --provider Sqlite
    if ($LASTEXITCODE -ne 0) { throw "Sqlite migrations add failed" }

    $env:EfActiveProvider = "Postgres"
    dotnet ef migrations add $Name --context AppDbContext `
      --output-dir Persistence/Migrations.Postgres --namespace MyApp.Persistence.Migrations.Postgres -- --provider Postgres
    if ($LASTEXITCODE -ne 0) { throw "Postgres migrations add failed" }

    $env:EfActiveProvider = "Sqlite"
    dotnet ef migrations script --context AppDbContext `
      --output Persistence/schema.sql -- --provider Sqlite
    if ($LASTEXITCODE -ne 0) { throw "Sqlite migrations script failed" }

    $env:EfActiveProvider = "Postgres"
    dotnet ef migrations script --context AppDbContext --idempotent `
      --output Persistence/schema.postgres.sql -- --provider Postgres
    if ($LASTEXITCODE -ne 0) { throw "Postgres migrations script failed" }

    Write-Host "Regenerated migrations + schema scripts for both providers."
} finally {
    Remove-Item Env:EfActiveProvider -ErrorAction SilentlyContinue
    Pop-Location
}
