#!/usr/bin/env bash
# Creates this machine's local development secrets. Run once after cloning.
#
# Generates a random SQL Server SA password and writes:
#   .secrets/sql_sa_password              Docker Compose secret for the SQL Server container
#   .secrets/Database__ConnectionString   Docker Compose secret mounted into the API container
#   user-secrets Database:ConnectionString for `dotnet run` of the API against localhost:1433
#
# .secrets/ is gitignored and user-secrets live outside the repository, so nothing generated
# here can be committed. Cloud environments get their secrets from the vault instead.
#
# Usage: scripts/init-local-secrets.sh [--force]
#   --force  replace existing secrets. The SQL Server volume keeps the old SA password, so run
#            `docker compose down -v` first (this deletes the local database).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
secrets_dir="$repo_root/.secrets"
api_project="$repo_root/src/Services/ReportingTool.Api"

if [[ -f "$secrets_dir/sql_sa_password" && "${1:-}" != "--force" ]]; then
  echo "Local secrets already exist in .secrets/. Use --force to replace them (after docker compose down -v)."
  exit 0
fi

mkdir -p "$secrets_dir"

# 24 random characters that need no escaping in a connection string or shell, plus a fixed
# suffix so SQL Server's complexity policy (3 of 4 character classes) always passes.
password="$(LC_ALL=C tr -dc 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789' < /dev/urandom | head -c 24)-Rt7"
options="Database=ReportingTool;User Id=sa;Encrypt=True;TrustServerCertificate=True"

# printf without a newline: the configuration key-per-file provider would keep it in the value.
printf '%s' "$password" > "$secrets_dir/sql_sa_password"
printf '%s' "Server=sqlserver,1433;$options;Password=$password" > "$secrets_dir/Database__ConnectionString"
# Bind-mounted Compose secrets keep host permissions; the containers run as non-root users.
chmod 644 "$secrets_dir"/*

dotnet user-secrets set "Database:ConnectionString" "Server=localhost,${RT_SQL_PORT:-1433};$options;Password=$password" --project "$api_project" > /dev/null

echo "Local secrets created:"
echo "  .secrets/sql_sa_password, .secrets/Database__ConnectionString (Docker Compose)"
echo "  user-secrets Database:ConnectionString (dotnet run of ReportingTool.Api)"
echo "Next: docker compose up --build, then open http://localhost:8080"
