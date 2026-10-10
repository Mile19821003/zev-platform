#!/usr/bin/env bash
# Disaster recovery onto a (new) host: restores the Zev database and the file storage from a backup set,
# then starts the full stack. Refuses to overwrite an existing Zev database.
#
# Usage: scripts/restore-database.sh <Zev_...bak> [<Zev_....files.tar.gz>]
#   Both arguments are paths on this host (e.g. copied back from off-host storage).
# Prerequisites: .env.prod present, images buildable; the script starts the db service itself.
set -euo pipefail
source "$(dirname "$0")/lib/zev-ops.sh"

[ $# -ge 1 ] && [ -f "$1" ] || { echo "usage: $0 <backup.bak> [<files.tar.gz>]" >&2; exit 2; }
bak_host="$1"
files_host="${2:-}"
[ -z "$files_host" ] || [ -f "$files_host" ] || { echo "files archive not found: $files_host" >&2; exit 2; }

start_log restore
started=$(date +%s)
trap 'log "RESTORE FAILED (line $LINENO) after $(( $(date +%s) - started ))s"' ERR

log "starting db service"
dc up -d --wait db
require_db
if [ "$(sql_value "SELECT CASE WHEN DB_ID(N'$ZEV_DB_NAME') IS NULL THEN 0 ELSE 1 END")" = "1" ]; then
    die "database $ZEV_DB_NAME already exists; this script only restores into an empty instance (see OPERATIONAL_BACKUP_RESTORE.md)"
fi
dc stop api proxy > /dev/null 2>&1 || true

name=$(basename "$bak_host")
bak="$BACKUP_ROOT/drill/$name"
dc cp "$(host_path "$bak_host")" "db:$bak" > /dev/null
sql -Q "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM" | tr -d '\r' | sed '/^$/d' >> "$LOG_FILE"
log "backup verified: $name"

moves=$(sql -h -1 -W -Q "SET NOCOUNT ON; DECLARE @f TABLE (LogicalName nvarchar(128), PhysicalName nvarchar(260), Type char(1), FileGroupName nvarchar(128), Size numeric(20,0), MaxSize numeric(20,0), FileId bigint, CreateLSN numeric(25,0), DropLSN numeric(25,0), UniqueId uniqueidentifier, ReadOnlyLSN numeric(25,0), ReadWriteLSN numeric(25,0), BackupSizeInBytes bigint, SourceBlockSize int, FileGroupId int, LogGroupGUID uniqueidentifier, DifferentialBaseLSN numeric(25,0), DifferentialBaseGUID uniqueidentifier, IsReadOnly bit, IsPresent bit, TDEThumbprint varbinary(32), SnapshotUrl nvarchar(360)); INSERT @f EXEC(N'RESTORE FILELISTONLY FROM DISK = N''$bak'''); SELECT STRING_AGG(N'MOVE N''' + REPLACE(LogicalName, '''', '''''') + N''' TO N''/var/opt/mssql/data/${ZEV_DB_NAME}_' + CAST(FileId AS nvarchar(10)) + CASE Type WHEN 'L' THEN N'.ldf' ELSE N'.mdf' END + N'''', N', ') FROM @f" | tr -d '\r' | sed '/^$/d' | tail -n 1)
sql -Q "RESTORE DATABASE [$ZEV_DB_NAME] FROM DISK = N'$bak' WITH $moves, RECOVERY, STATS = 25" | tr -d '\r' | sed '/^$/d' >> "$LOG_FILE"
helper "rm -f /backups/drill/$name"
log "database $ZEV_DB_NAME restored"
checkdb=$(sql -Q "DBCC CHECKDB([$ZEV_DB_NAME]) WITH NO_INFOMSGS, ALL_ERRORMSGS" 2>&1 | tr -d '\r' | sed '/^$/d' || true)
[ -z "$checkdb" ] || { echo "$checkdb" >> "$LOG_FILE"; die "DBCC CHECKDB reported errors on the restored database"; }
log "DBCC CHECKDB: no errors"

if [ -n "$files_host" ]; then
    count=$(docker run --rm -i -v "$FILES_VOLUME:/files" "$ZEV_HELPER_IMAGE" sh -c \
        "set -e; tar -xzf - -C /files; chown -R 1654:1654 /files; find /files -type f | wc -l" < "$files_host")
    log "file storage restored into $FILES_VOLUME ($count files)"
else
    log "WARNING: no files archive given; uploaded documents are not restored"
fi

# migrate re-binds the application login (new server, new SID) and applies migrations newer than the backup.
log "starting full stack (migrate, api, web, proxy)"
dc up -d --wait
log "migrate: $(dc logs migrate 2>/dev/null | grep -o 'applied migrations.*' | tail -n 1)"
log "RESTORE OK in $(( $(date +%s) - started ))s; verify login and data before opening to users. log=$LOG_FILE"
