#!/usr/bin/env bash
# Regenerate both providers' migration histories + embedded schema scripts
# after entity changes. Run from the za-vertical-slice root.
#
# Usage: ./tools/regen-schema.sh [MigrationName]
#   MigrationName defaults to "Update".
#
# Both providers must stay in lockstep — running this script regenerates
# Migrations.Sqlite, Migrations.Postgres, schema.sql, and schema.postgres.sql
# in one pass so adopters don't have to remember the EfActiveProvider knob.
set -euo pipefail
NAME="${1:-Update}"

cd "$(dirname "$0")/../src/MyApp"

# Provider must be set as an env var (not -p:...) because `dotnet ef` aliases
# -p to --project. The MyApp.csproj's MSBuild conditionals on
# $(EfActiveProvider) exclude the wrong provider's migration classes from
# compilation, so `dotnet ef migrations add/script` sees only the active
# provider's [Migration] classes.

EfActiveProvider=Sqlite dotnet ef migrations add "$NAME" --context AppDbContext \
  --output-dir Persistence/Migrations.Sqlite   --namespace MyApp.Persistence.Migrations.Sqlite   -- --provider Sqlite
EfActiveProvider=Postgres dotnet ef migrations add "$NAME" --context AppDbContext \
  --output-dir Persistence/Migrations.Postgres --namespace MyApp.Persistence.Migrations.Postgres -- --provider Postgres

# Sqlite doesn't support --idempotent; the schema.sql instead relies on
# `CREATE TABLE IF NOT EXISTS __EFMigrationsHistory` plus the runtime
# ApplyEmbeddedSchemaAsync check.
EfActiveProvider=Sqlite dotnet ef migrations script --context AppDbContext \
  --output Persistence/schema.sql           -- --provider Sqlite
EfActiveProvider=Postgres dotnet ef migrations script --context AppDbContext \
  --idempotent --output Persistence/schema.postgres.sql -- --provider Postgres

echo "Regenerated migrations + schema scripts for both providers."
