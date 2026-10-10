#!/usr/bin/env bash
# Automated restore drill: restores a backup into the temporary database Zev_Drill on the running
# SQL Server, runs DBCC CHECKDB and schema checks, then drops Zev_Drill. Production data is not touched.
#
# Usage: scripts/run-restore-drill.sh [<backup>]
#   <backup>  name of a set in the backup volume (Zev_20261010T020000Z_daily[.bak]),
#             or a path to a .bak file on this host (e.g. fetched from off-host storage).
#             Default: newest backup in the volume.
# Exit code 0 = PASS, non-zero = FAIL. Log: logs/restore-drill-<timestamp>.log
set -euo pipefail
source "$(dirname "$0")/lib/zev-ops.sh"

DRILL_DB=Zev_Drill
CORE_TABLES="Organizations Zevs Users Memberships Buildings PropertyUnits People Ownerships Funds Charges Invoices Payments PaymentAllocations FundTransactions AuditLogs Hodograms"

start_log restore-drill
started=$(date +%s)
status=FAIL
copied=""
cleanup() {
    if [ -n "$copied" ]; then helper "rm -f '$copied'" || true; fi
    if sql_value "SELECT DB_ID(N'$DRILL_DB')" 2>/dev/null | grep -qv NULL; then
        sql -Q "ALTER DATABASE [$DRILL_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DRILL_DB];" > /dev/null \
            && log "dropped temporary database $DRILL_DB" || log "WARNING: could not drop $DRILL_DB"
    fi
    log "RESTORE DRILL $status duration=$(( $(date +%s) - started ))s log=$LOG_FILE"
}
trap cleanup EXIT
trap 'log "step failed (line $LINENO)"' ERR

require_db
source_arg="${1:-}"
if [ -z "$source_arg" ]; then
    source_arg=$(helper "ls -1 /backups/db/*.bak 2>/dev/null | sort | tail -n 1")
    [ -n "$source_arg" ] || die "no backups in volume $BACKUP_VOLUME"
    bak="$BACKUP_ROOT/db/$(basename "$source_arg")"
elif [ -f "$source_arg" ]; then
    # Host file (e.g. downloaded from off-host storage): copy it into the backup volume for SQL Server.
    copied="/backups/drill/$(basename "$source_arg")"
    dc cp "$(host_path "$source_arg")" "db:$BACKUP_ROOT/drill/$(basename "$source_arg")" > /dev/null
    bak="$BACKUP_ROOT/drill/$(basename "$source_arg")"
    log "copied host file $source_arg into the backup volume"
else
    bak="$BACKUP_ROOT/db/${source_arg%.bak}.bak"
fi
set_name=$(basename "$bak" .bak)
log "drill source: $bak"

# a) integrity of the file itself
if [ -z "$copied" ] && helper "test -f /backups/db/$set_name.sha256"; then
    helper "cd /backups && grep ' db/$set_name.bak' db/$set_name.sha256 | sha256sum -c -" >> "$LOG_FILE" \
        || die "sha256 mismatch for $set_name.bak"
    log "sha256 matches the value recorded at backup time"
fi
sql -Q "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM" | tr -d '\r' | sed '/^$/d' >> "$LOG_FILE"
log "RESTORE VERIFYONLY WITH CHECKSUM passed"

# b) restore into the temporary database
if sql_value "SELECT DB_ID(N'$DRILL_DB')" | grep -qv NULL; then
    log "removing leftover $DRILL_DB from an earlier interrupted drill"
    sql -Q "ALTER DATABASE [$DRILL_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DRILL_DB];" > /dev/null
fi
moves=$(sql -h -1 -W -s '|' -Q "SET NOCOUNT ON; DECLARE @f TABLE (LogicalName nvarchar(128), PhysicalName nvarchar(260), Type char(1), FileGroupName nvarchar(128), Size numeric(20,0), MaxSize numeric(20,0), FileId bigint, CreateLSN numeric(25,0), DropLSN numeric(25,0), UniqueId uniqueidentifier, ReadOnlyLSN numeric(25,0), ReadWriteLSN numeric(25,0), BackupSizeInBytes bigint, SourceBlockSize int, FileGroupId int, LogGroupGUID uniqueidentifier, DifferentialBaseLSN numeric(25,0), DifferentialBaseGUID uniqueidentifier, IsReadOnly bit, IsPresent bit, TDEThumbprint varbinary(32), SnapshotUrl nvarchar(360)); INSERT @f EXEC(N'RESTORE FILELISTONLY FROM DISK = N''$bak'''); SELECT STRING_AGG(N'MOVE N''' + REPLACE(LogicalName, '''', '''''') + N''' TO N''/var/opt/mssql/data/${DRILL_DB}_' + CAST(FileId AS nvarchar(10)) + CASE Type WHEN 'L' THEN N'.ldf' ELSE N'.mdf' END + N'''', N', ') FROM @f" | tr -d '\r' | sed '/^$/d' | tail -n 1)
[ -n "$moves" ] || die "could not read the file list of the backup"
restore_started=$(date +%s)
sql -Q "RESTORE DATABASE [$DRILL_DB] FROM DISK = N'$bak' WITH $moves, RECOVERY, STATS = 25" | tr -d '\r' | sed '/^$/d' >> "$LOG_FILE"
log "restored into $DRILL_DB in $(( $(date +%s) - restore_started ))s"

# c) physical and logical consistency
checkdb_started=$(date +%s)
checkdb=$(sql -Q "DBCC CHECKDB([$DRILL_DB]) WITH NO_INFOMSGS, ALL_ERRORMSGS" 2>&1 | tr -d '\r' | sed '/^$/d' || true)
[ -z "$checkdb" ] || { echo "$checkdb" >> "$LOG_FILE"; die "DBCC CHECKDB reported errors"; }
log "DBCC CHECKDB: no errors ($(( $(date +%s) - checkdb_started ))s)"

# Schema: applied migrations must be known to this code base, and the core tables must exist.
applied=$(sql -d "$DRILL_DB" -h -1 -W -Q "SET NOCOUNT ON; SELECT MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId" | tr -d '\r' | sed '/^$/d')
[ -n "$applied" ] || die "__EFMigrationsHistory is empty or missing"
log "applied migrations: $(echo "$applied" | wc -l | tr -d ' ') (latest $(echo "$applied" | tail -n 1))"
migrations_dir="$ZEV_ROOT/src/Zev.Api/Data/Migrations"
if [ -d "$migrations_dir" ]; then
    known=$(ls "$migrations_dir" | grep -E '^[0-9]{14}_.+\.cs$' | grep -v '\.Designer\.cs$' | sed 's/\.cs$//' | sort)
    unknown=$(comm -23 <(echo "$applied" | sort) <(echo "$known"))
    [ -z "$unknown" ] || die "backup contains migrations unknown to this code base: $unknown"
    pending=$(comm -13 <(echo "$applied" | sort) <(echo "$known"))
    [ -z "$pending" ] && log "schema version matches the code base" \
        || log "NOTE: backup is older than the code; the migrate step would apply: $(echo "$pending" | tr '\n' ' ')"
fi
missing=""
for table in $CORE_TABLES; do
    [ "$(sql_value "SELECT CASE WHEN OBJECT_ID(N'[$DRILL_DB].[dbo].[$table]') IS NULL THEN 0 ELSE 1 END")" = "1" ] || missing="$missing $table"
done
[ -z "$missing" ] || die "core tables missing:$missing"
tables=$(sql_value "SELECT COUNT(*) FROM [$DRILL_DB].sys.tables")
counts=$(sql_value "SELECT CONCAT('organizations=', (SELECT COUNT(*) FROM [$DRILL_DB].dbo.Organizations), ' zevs=', (SELECT COUNT(*) FROM [$DRILL_DB].dbo.Zevs), ' users=', (SELECT COUNT(*) FROM [$DRILL_DB].dbo.Users), ' invoices=', (SELECT COUNT(*) FROM [$DRILL_DB].dbo.Invoices), ' payments=', (SELECT COUNT(*) FROM [$DRILL_DB].dbo.Payments), ' auditLogs=', (SELECT COUNT(*) FROM [$DRILL_DB].dbo.AuditLogs))")
log "schema check passed: $tables tables, all core tables present; rows: $counts"

status=PASS
