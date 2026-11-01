#!/usr/bin/env bash
# Applies every migration to an empty SQL Server in Docker, steps back one migration, then applies it again.
# For sessions without the local SQL Express. It proves the migration runs both ways on the schema, not on real data:
# a migration that moves data still needs the check on a copy of SignFlowDb_Dev (docs/rework/dev-guide.md).
#
#   scripts/check-migration.sh            # last migration
#   scripts/check-migration.sh <Previous>  # step back to this migration instead
set -euo pipefail
cd "$(dirname "$0")/../src/backend"

PASSWORD="Check-$(date +%s)-Aa1"
NAME="legaldesk-migration-check"
CONN="Server=localhost,14333;Database=MigrationCheck;User Id=sa;Password=${PASSWORD};TrustServerCertificate=True"

cleanup() { docker rm -f "$NAME" >/dev/null 2>&1 || true; }
trap cleanup EXIT
cleanup
docker run -d --name "$NAME" -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=${PASSWORD}" -p 14333:1433 \
  mcr.microsoft.com/mssql/server:2022-latest >/dev/null
echo "Waiting for SQL Server..."
for _ in $(seq 1 60); do
  docker exec "$NAME" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$PASSWORD" -Q "SELECT 1" >/dev/null 2>&1 && break
  sleep 2
done

dotnet build SignFlow.Api >/dev/null
ef() { dotnet ef database update "$@" -p SignFlow.Infrastructure -s SignFlow.Api --connection "$CONN" --no-build; }

PREVIOUS="${1:-$(ls SignFlow.Infrastructure/Migrations/*.cs | grep -v -e Designer -e Snapshot | sort | tail -2 | head -1 | xargs basename | sed 's/\.cs$//')}"
echo "Up to latest";      ef
echo "Down to $PREVIOUS"; ef "$PREVIOUS"
echo "Up again";          ef
echo "Migration check passed."
