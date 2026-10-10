#!/usr/bin/env bash
# Daily full backup of the Zev database and the file storage volume, with verification,
# rotation (7 daily / 4 weekly / 12 monthly) and optional off-host copy.
#
# Usage: scripts/backup.sh [daily|weekly|monthly]      (class is derived from the date if omitted)
# Env:   ZEV_BACKUP_KEEP_DAILY=7 ZEV_BACKUP_KEEP_WEEKLY=4 ZEV_BACKUP_KEEP_MONTHLY=12
#        ZEV_BACKUP_COMPRESSION=auto|on|off   (auto: off only on Express, which cannot compress)
#        ZEV_OFFSITE_DIR=/mnt/zev-offsite      (mounted network share; files are copied there)
#        ZEV_OFFSITE_COMMAND='rclone copy "$ZEV_BACKUP_EXPORT" remote:zev-backups'
#                                              (runs with ZEV_BACKUP_EXPORT = local dir holding this backup set)
set -euo pipefail
source "$(dirname "$0")/lib/zev-ops.sh"

KEEP_DAILY="${ZEV_BACKUP_KEEP_DAILY:-7}"
KEEP_WEEKLY="${ZEV_BACKUP_KEEP_WEEKLY:-4}"
KEEP_MONTHLY="${ZEV_BACKUP_KEEP_MONTHLY:-12}"
COMPRESSION="${ZEV_BACKUP_COMPRESSION:-auto}"

start_log backup
trap 'log "BACKUP FAILED (line $LINENO)"' ERR
started=$(date +%s)

class="${1:-}"
if [ -z "$class" ]; then
    if [ "$(date -u +%d)" = "01" ]; then class=monthly
    elif [ "$(date -u +%u)" = "7" ]; then class=weekly
    else class=daily; fi
fi
case "$class" in daily|weekly|monthly) ;; *) die "class must be daily, weekly or monthly" ;; esac

require_db
name="Zev_$(date -u +%Y%m%dT%H%M%SZ)_${class}"
bak="$BACKUP_ROOT/db/$name.bak"
log "backup $name (class $class) from project $ZEV_PROJECT, volume $BACKUP_VOLUME"

edition=$(sql_value "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)")
recovery=$(sql_value "SELECT recovery_model_desc FROM sys.databases WHERE name = N'$ZEV_DB_NAME'")
[ -n "$recovery" ] || die "database $ZEV_DB_NAME not found"
[ "$recovery" = "SIMPLE" ] || log "WARNING: recovery model is $recovery without log backups; the transaction log will grow"
options="INIT, FORMAT, CHECKSUM, STATS = 25, NAME = N'$name'"
case "$COMPRESSION" in
    on) options="$options, COMPRESSION" ;;
    off) ;;
    auto) if [ "$edition" = "4" ]; then log "Express edition: backup compression not supported, writing uncompressed"
          else options="$options, COMPRESSION"; fi ;;
    *) die "ZEV_BACKUP_COMPRESSION must be auto, on or off" ;;
esac

sql -Q "BACKUP DATABASE [$ZEV_DB_NAME] TO DISK = N'$bak' WITH $options" | tr -d '\r' | sed '/^$/d' >> "$LOG_FILE"
sql -Q "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM" | tr -d '\r' | sed '/^$/d' >> "$LOG_FILE"
log "database backup written and verified (RESTORE VERIFYONLY WITH CHECKSUM)"

# File storage (uploaded documents) is not in the database backup; archive it in the same backup set.
helper "set -e
tar -czf /backups/files/$name.files.tar.gz -C /files .
tar -tzf /backups/files/$name.files.tar.gz > /dev/null
cd /backups && sha256sum db/$name.bak files/$name.files.tar.gz > db/$name.sha256
chown 10001:0 db/$name.bak db/$name.sha256 files/$name.files.tar.gz"
log "file storage archived; checksums: $(helper "cat /backups/db/$name.sha256" | awk '{print $1}' | tr '\n' ' ')"

# Rotation: keep the newest N sets per class (names sort chronologically).
for pair in "daily:$KEEP_DAILY" "weekly:$KEEP_WEEKLY" "monthly:$KEEP_MONTHLY"; do
    rotate_class="${pair%%:*}"; keep="${pair##*:}"
    removed=$(helper "cd /backups/db && ls -1 Zev_*_${rotate_class}.bak 2>/dev/null | sort -r | tail -n +$((keep + 1)) | while read -r old; do
        set_name=\${old%.bak}; rm -f \$set_name.bak \$set_name.sha256 ../files/\$set_name.files.tar.gz; echo \$set_name; done")
    [ -z "$removed" ] || log "rotation ($rotate_class, keep $keep) removed: $(echo "$removed" | tr '\n' ' ')"
done

if [ -n "${ZEV_OFFSITE_DIR:-}${ZEV_OFFSITE_COMMAND:-}" ]; then
    export_dir=$(mktemp -d)
    trap 'rm -rf "$export_dir"' EXIT
    for file in "db/$name.bak" "db/$name.sha256" "files/$name.files.tar.gz"; do
        dc cp "db:$BACKUP_ROOT/$file" "$(host_path "$export_dir")/" > /dev/null
    done
    (cd "$export_dir" && sed 's# db/# #; s# files/# #' "$name.sha256" | sha256sum -c --quiet -) || die "exported copy does not match checksums"
    if [ -n "${ZEV_OFFSITE_DIR:-}" ]; then
        mkdir -p "$ZEV_OFFSITE_DIR"
        cp "$export_dir"/* "$ZEV_OFFSITE_DIR/"
        log "copied backup set to $ZEV_OFFSITE_DIR"
    fi
    if [ -n "${ZEV_OFFSITE_COMMAND:-}" ]; then
        ZEV_BACKUP_EXPORT="$export_dir" bash -c "$ZEV_OFFSITE_COMMAND"
        log "off-host command completed"
    fi
else
    log "WARNING: no ZEV_OFFSITE_DIR / ZEV_OFFSITE_COMMAND configured; backup exists only on this host"
fi

size=$(helper "du -k /backups/db/$name.bak /backups/files/$name.files.tar.gz" | awk '{s+=$1} END {print s}')
log "BACKUP OK $name size=${size}KB duration=$(( $(date +%s) - started ))s log=$LOG_FILE"
