namespace Zev.Api.Domain;

// Records that are deleted by marking them (shown in Administration → "Obrisani redovi" and restorable there).
// Deleted rows are hidden by a global query filter; only records without live dependent data can be deleted —
// anything still in use is deactivated instead (IsActive), which keeps it in history and reports.
public interface ISoftDeletable
{
    Guid Id { get; }
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedByUserId { get; set; }
}
