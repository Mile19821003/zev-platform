using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class SystemErrorEndpoints
{
    // Error reports from the web app: also before sign-in, so anonymous but rate limited and size limited.
    public static RouteGroupBuilder MapClientErrorEndpoint(this RouteGroupBuilder api)
    {
        api.MapPost("/system/errors/client", async (ClientErrorReport request, HttpContext context, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "Naziv i poruka greške su obavezni." });
            var entry = new SystemErrorLog
            {
                Title = SystemErrorLog.Clip(request.Title.Trim(), SystemErrorLog.TitleLength),
                Source = SystemErrorSource.Frontend,
                ErrorType = SystemErrorLog.Clip(string.IsNullOrWhiteSpace(request.ErrorType) ? "Error" : request.ErrorType.Trim(),
                    SystemErrorLog.ErrorTypeLength),
                Message = SystemErrorLog.Clip(request.Message.Trim(), SystemErrorLog.MessageLength),
                StackTrace = string.IsNullOrWhiteSpace(request.StackTrace) ? null
                    : SystemErrorLog.Clip(request.StackTrace, SystemErrorLog.StackTraceLength),
                RequestPath = string.IsNullOrWhiteSpace(request.Url) ? null : SystemErrorLog.Clip(request.Url, SystemErrorLog.PathLength),
                UserId = Guid.TryParse(context.User.FindFirstValue("sub"), out var userId) ? userId : null
            };
            database.SystemErrorLogs.Add(entry);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Accepted(value: new { id = entry.Id });
        }).AllowAnonymous().RequireRateLimiting("client-errors");
        return api;
    }

    // Review for system administrators (the /system group requires the SystemAdministrator policy).
    public static RouteGroupBuilder MapSystemErrorAdminEndpoints(this RouteGroupBuilder system)
    {
        system.MapGet("/errors", async (string? status, int? page, int? pageSize, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.SystemErrorLogs.AsNoTracking();
            switch (status?.ToLowerInvariant())
            {
                case null or "" or "active": query = query.Where(item => item.Status == SystemErrorStatus.Active); break;
                case "resolved": query = query.Where(item => item.Status == SystemErrorStatus.Resolved); break;
                case "all": break;
                default: return Results.BadRequest(new { error = "Status: active, resolved ili all." });
            }
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.Title, source = item.Source.ToString(), item.ErrorType, item.Message,
                    item.StackTrace, item.RequestPath, item.UserId, status = item.Status.ToString(), item.AdminNotes,
                    item.CreatedAt, item.ResolvedAt, item.ResolvedByUserId })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        system.MapPut("/errors/{id:int}", async (int id, UpdateSystemErrorRequest request, TenantAccess access,
            ZevDbContext database, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (request.AdminNotes?.Length > SystemErrorLog.NotesLength)
                return Results.BadRequest(new { error = "Bilješka može imati najviše 2000 znakova." });
            var entry = await database.SystemErrorLogs.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (entry is null) return Results.NotFound();
            var before = JsonSerializer.Serialize(new { status = entry.Status.ToString(), entry.AdminNotes });
            entry.AdminNotes = string.IsNullOrWhiteSpace(request.AdminNotes) ? null : request.AdminNotes.Trim();
            if (request.Resolved && entry.Status != SystemErrorStatus.Resolved)
            {
                entry.Status = SystemErrorStatus.Resolved;
                entry.ResolvedAt = DateTimeOffset.UtcNow;
                entry.ResolvedByUserId = access.CurrentUserId;
            }
            else if (!request.Resolved && entry.Status == SystemErrorStatus.Resolved)
            {
                entry.Status = SystemErrorStatus.Active;
                entry.ResolvedAt = null;
                entry.ResolvedByUserId = null;
            }
            database.AuditLogs.Add(new AuditLog
            {
                ActorUserId = access.CurrentUserId, EntityType = nameof(SystemErrorLog), EntityId = id.ToString(),
                Action = "SystemErrorUpdated", BeforeJson = before,
                AfterJson = JsonSerializer.Serialize(new { status = entry.Status.ToString(), entry.AdminNotes }),
                CorrelationId = context.TraceIdentifier
            });
            await database.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { entry.Id, status = entry.Status.ToString(), entry.AdminNotes, entry.ResolvedAt,
                entry.ResolvedByUserId });
        });
        return system;
    }
}

public sealed record ClientErrorReport(string? Title, string? ErrorType, string? Message, string? StackTrace, string? Url);
public sealed record UpdateSystemErrorRequest(bool Resolved, string? AdminNotes);
