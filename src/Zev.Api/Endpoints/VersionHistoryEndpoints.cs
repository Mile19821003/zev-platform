using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class VersionHistoryEndpoints
{
    // Installed versions, newest first (the /system group requires the SystemAdministrator policy).
    public static RouteGroupBuilder MapVersionHistoryEndpoints(this RouteGroupBuilder system)
    {
        system.MapGet("/versions", async (ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var rows = await database.VersionHistory.AsNoTracking().ToListAsync(cancellationToken);
            return Results.Ok(rows.OrderByDescending(item => AppVersionHistory.Parse(item.Version))
                .Select(item => new { item.Version, item.Status, item.Title, item.InstalledAt, item.InstalledBy,
                    item.AdminNotes, item.NotesUpdatedAt, item.NotesUpdatedByUserId }));
        });

        system.MapPut("/versions/{version}/notes", async (string version, VersionNotesRequest request, TenantAccess access,
            ZevDbContext database, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (request.AdminNotes?.Length > AppVersionHistory.NotesLength)
                return Results.BadRequest(new { error = "Bilješka može imati najviše 2000 znakova." });
            var row = await database.VersionHistory.SingleOrDefaultAsync(item => item.Version == version, cancellationToken);
            if (row is null) return Results.NotFound();
            var before = row.AdminNotes;
            row.AdminNotes = string.IsNullOrWhiteSpace(request.AdminNotes) ? null : request.AdminNotes.Trim();
            row.NotesUpdatedAt = DateTimeOffset.UtcNow;
            row.NotesUpdatedByUserId = access.CurrentUserId;
            database.AuditLogs.Add(new AuditLog
            {
                ActorUserId = access.CurrentUserId, EntityType = nameof(AppVersionHistory), EntityId = version,
                Action = "VersionNotesUpdated", BeforeJson = JsonSerializer.Serialize(new { AdminNotes = before }),
                AfterJson = JsonSerializer.Serialize(new { row.AdminNotes }), CorrelationId = context.TraceIdentifier
            });
            await database.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { row.Version, row.AdminNotes, row.NotesUpdatedAt, row.NotesUpdatedByUserId });
        });
        return system;
    }
}

public sealed record VersionNotesRequest(string? AdminNotes);
