using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;
using Zev.Api.Storage;

namespace Zev.Api.Endpoints;

public static class StoredFileEndpoints
{
    public static RouteGroupBuilder MapStoredFileEndpoints(this RouteGroupBuilder api)
    {
        var files = api.MapGroup("/zevs/{zevId:guid}/files");
        files.MapPost("", async (Guid zevId, UploadFileRequest request, TenantAccess access,
            ZevDbContext database, IFileStorage storage, CancellationToken cancellationToken) =>
        {
            if (!await access.CanReadZevAsync(zevId, cancellationToken) || access.CurrentUserId is not Guid userId)
                return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.ContentBase64) || request.ContentBase64.Length > 7_000_000
                || !LocalFileStorage.ValidFileName(request.FileName, request.ContentType)) return Results.BadRequest();
            byte[] content;
            try { content = Convert.FromBase64String(request.ContentBase64); }
            catch (FormatException) { return Results.BadRequest(); }
            if (!LocalFileStorage.ValidContent(content, request.ContentType)) return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var key = await storage.SaveAsync(content, request.ContentType, cancellationToken);
            var file = new StoredFile
            {
                OrganizationId = organizationId, ZevId = zevId, UploadedByUserId = userId,
                OriginalFileName = Path.GetFileName(request.FileName), ContentType = request.ContentType,
                Length = content.Length, Sha256 = Convert.ToHexString(SHA256.HashData(content)), StorageKey = key
            };
            database.StoredFiles.Add(file);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Created($"/api/zevs/{zevId}/files/{file.Id}", new
                {
                    file.Id, file.OriginalFileName, file.ContentType, file.Length
                });
            }
            catch
            {
                await storage.RemoveAsync(key, cancellationToken);
                throw;
            }
        }).RequireRateLimiting("upload");

        files.MapGet("/{fileId:guid}", async (Guid zevId, Guid fileId, TenantAccess access,
            ZevDbContext database, IFileStorage storage, CancellationToken cancellationToken) =>
        {
            if (!await access.CanReadZevAsync(zevId, cancellationToken)) return Results.NotFound();
            var file = await database.StoredFiles.AsNoTracking().SingleOrDefaultAsync(item => item.ZevId == zevId
                && item.Id == fileId, cancellationToken);
            if (file is null) return Results.NotFound();
            if (await database.ManagedDocuments.AnyAsync(item => item.ZevId == zevId
                && item.StoredFileId == fileId, cancellationToken)) return Results.NotFound();
            var expense = await database.Expenses.AnyAsync(item => item.ZevId == zevId
                && item.DocumentId == fileId, cancellationToken);
            var legal = await database.LegalCaseFiles.AnyAsync(item => item.ZevId == zevId
                && item.StoredFileId == fileId, cancellationToken);
            if (expense && !await access.HasPermissionAsync(zevId, "Expenses.View", cancellationToken)) return Results.NotFound();
            if (legal && !await access.HasPermissionAsync(zevId, "Legal.Documents", cancellationToken)) return Results.NotFound();
            var allowed = file.UploadedByUserId == access.CurrentUserId
                || (expense && await access.HasPermissionAsync(zevId, "Expenses.View", cancellationToken))
                || (legal && await access.HasPermissionAsync(zevId, "Legal.Documents", cancellationToken))
                || (await access.HasPermissionAsync(zevId, "Maintenance.View", cancellationToken)
                    && (await database.MaintenanceTicketFiles.AnyAsync(item => item.ZevId == zevId
                            && item.StoredFileId == fileId, cancellationToken)
                        || await database.WorkOrderFiles.AnyAsync(item => item.ZevId == zevId
                            && item.StoredFileId == fileId, cancellationToken)));
            if (!allowed) return Results.NotFound();
            var content = await storage.ReadAsync(file.StorageKey, cancellationToken);
            return Results.File(content, file.ContentType, file.OriginalFileName);
        });
        return api;
    }
}

public sealed record UploadFileRequest(string FileName, string ContentType, string ContentBase64);
