#!/usr/bin/env bash
# Shared helpers for the production operations scripts (backup, restore drill, restore).
# Sourced, not executed. Configuration comes from the environment; see OPERATIONAL_BACKUP_RESTORE.md.

ZEV_ROOT="${ZEV_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
# Git Bash on Windows (test machines only): give docker.exe a C:/... path instead of /c/...
if command -v cygpath > /dev/null 2>&1; then ZEV_ROOT="$(cygpath -m "$ZEV_ROOT")"; fi
ZEV_COMPOSE_FILE="${ZEV_COMPOSE_FILE:-$ZEV_ROOT/compose.prod.yaml}"
ZEV_ENV_FILE="${ZEV_ENV_FILE:-$ZEV_ROOT/.env.prod}"
if command -v cygpath > /dev/null 2>&1; then
    ZEV_COMPOSE_FILE="$(cygpath -m "$ZEV_COMPOSE_FILE")"; ZEV_ENV_FILE="$(cygpath -m "$ZEV_ENV_FILE")"
fi
ZEV_PROJECT="${ZEV_PROJECT:-zev}"
ZEV_LOG_DIR="${ZEV_LOG_DIR:-$ZEV_ROOT/logs}"
ZEV_DB_NAME="${ZEV_DB_NAME:-Zev}"
ZEV_HELPER_IMAGE="${ZEV_HELPER_IMAGE:-busybox:1.37}"
BACKUP_ROOT=/var/opt/mssql/backups
# Git Bash on Windows rewrites /var/... arguments into Windows paths; harmless on Linux.
export MSYS_NO_PATHCONV=1

if [ -f "$ZEV_ENV_FILE" ]; then
    ZEV_VOLUME_PREFIX="${ZEV_VOLUME_PREFIX:-$(sed -n 's/^ZEV_VOLUME_PREFIX=//p' "$ZEV_ENV_FILE" | tail -n 1)}"
fi
ZEV_VOLUME_PREFIX="${ZEV_VOLUME_PREFIX:-zev}"
BACKUP_VOLUME="${ZEV_VOLUME_PREFIX}_db_backups"
FILES_VOLUME="${ZEV_VOLUME_PREFIX}_files"

LOG_FILE=""

# Host path as docker.exe expects it (identity on Linux).
host_path() { if command -v cygpath > /dev/null 2>&1; then cygpath -m "$1"; else printf '%s\n' "$1"; fi; }

log() {
    local line
    line="$(date -u +%Y-%m-%dT%H:%M:%SZ) $*"
    echo "$line"
    if [ -n "$LOG_FILE" ]; then echo "$line" >> "$LOG_FILE"; fi
}

die() { log "ERROR: $*"; exit 1; }

start_log() {
    mkdir -p "$ZEV_LOG_DIR"
    LOG_FILE="$ZEV_LOG_DIR/$1-$(date -u +%Y%m%dT%H%M%SZ).log"
    : > "$LOG_FILE"
}

dc() { docker compose -p "$ZEV_PROJECT" -f "$ZEV_COMPOSE_FILE" --env-file "$ZEV_ENV_FILE" "$@"; }

# Runs sqlcmd inside the db container; the sa password never leaves the container environment.
sql() {
    dc exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I "$@"' sqlcmd "$@"
}

# Single scalar value, no headers or padding.
sql_value() { sql -h -1 -W -Q "SET NOCOUNT ON; $1" | tr -d '\r' | sed '/^$/d' | tail -n 1; }

# Runs a shell command in a throwaway container with the backup (rw) and file storage (ro) volumes mounted.
helper() {
    docker run --rm -v "$BACKUP_VOLUME:/backups" -v "$FILES_VOLUME:/files:ro" "$ZEV_HELPER_IMAGE" sh -c "$1"
}

require_db() {
    [ -f "$ZEV_ENV_FILE" ] || die "env file not found: $ZEV_ENV_FILE"
    dc ps --status running --services 2>/dev/null | grep -qx db || die "db service is not running (dc up -d db)"
    sql_value "SELECT 1" | grep -qx 1 || die "SQL Server is not answering"
}
