#!/bin/bash
# Usage (inside the migrate container):
#   migrate.sh          create database if missing, apply migrations, ensure application login
#   migrate.sh print    print the idempotent migration script for review
set -euo pipefail

if [ "${1:-apply}" = "print" ]; then
    cat /migrator/migrate.sql
    exit 0
fi

: "${DB_HOST:?}" "${DB_NAME:?}" "${SA_PASSWORD:?}" "${APP_DB_LOGIN:?}" "${APP_DB_PASSWORD:?}"
case "$APP_DB_PASSWORD$APP_DB_LOGIN" in
    *"'"*|*"]"*) echo "migrate: APP_DB_LOGIN/APP_DB_PASSWORD must not contain ' or ]" >&2; exit 2 ;;
esac

# sqlcmd reads the password from SQLCMDPASSWORD and scripting variables from the environment,
# so no secret appears on a command line.
export SQLCMDPASSWORD="$SA_PASSWORD"
sql() { /opt/mssql-tools18/bin/sqlcmd -S "$DB_HOST" -U sa -C -b -I "$@"; }

# After a restart (especially after a crash) the database can still be recovering although the server
# already accepts logins; wait until it can actually be opened, or until it is known not to exist.
for attempt in $(seq 1 60); do
    exists=$(sql -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$DB_NAME'" 2>/dev/null | tr -d '\r' | sed '/^$/d' | tail -n 1 || true)
    if [ "$exists" = "0" ]; then break; fi
    if [ "$exists" = "1" ] && sql -d "$DB_NAME" -Q "SET NOCOUNT ON; SELECT 1" > /dev/null 2>&1; then break; fi
    [ "$attempt" -lt 60 ] || { echo "migrate: database [$DB_NAME] could not be opened within 120 s" >&2; exit 1; }
    echo "migrate: database [$DB_NAME] not available yet (recovering), waiting"
    sleep 2
done

echo "migrate: ensuring database [$DB_NAME]"
# Same options EF Core uses when it creates the database (READ_COMMITTED_SNAPSHOT on). SIMPLE recovery
# matches the daily-full-backup policy (RPO 24 h); FULL without log backups would grow the log without bound.
sql -Q "IF DB_ID(N'$DB_NAME') IS NULL
BEGIN
    CREATE DATABASE [$DB_NAME];
    IF SERVERPROPERTY('EngineEdition') <> 5 EXEC(N'ALTER DATABASE [$DB_NAME] SET READ_COMMITTED_SNAPSHOT ON;');
    EXEC(N'ALTER DATABASE [$DB_NAME] SET RECOVERY SIMPLE;');
END"

before=$(sql -d "$DB_NAME" -h -1 -W -Q "SET NOCOUNT ON; IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [__EFMigrationsHistory]")
echo "migrate: applying idempotent migration script (applied before: $before)"
if ! sql -d "$DB_NAME" -i /migrator/migrate.sql > /tmp/migrate.out 2>&1; then
    echo "migrate: migration script FAILED (each migration runs in its own transaction and was rolled back):" >&2
    grep -A3 "^Msg " /tmp/migrate.out >&2 || tail -n 20 /tmp/migrate.out >&2
    exit 1
fi
after=$(sql -d "$DB_NAME" -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM [__EFMigrationsHistory]")
latest=$(sql -d "$DB_NAME" -h -1 -W -Q "SET NOCOUNT ON; SELECT TOP 1 MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId DESC")
echo "migrate: applied migrations $before -> $after (latest $latest)"

echo "migrate: ensuring application login [$APP_DB_LOGIN]"
sql -d "$DB_NAME" -i /migrator/app-login.sql
echo "migrate: done"
