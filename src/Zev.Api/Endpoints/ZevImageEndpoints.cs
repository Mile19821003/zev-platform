using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Security;
using Zev.Api.Storage;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Endpoints;

public sealed record UploadZevImageRequest(string FileName, string ContentType, string ContentBase64);

public static class ZevImageEndpoints
{
    public const int MaxBytes = 5_000_000;
    private static readonly Dictionary<string, string[]> Extensions = new()
    {
        ["image/jpeg"] = [".jpg", ".jpeg"], ["image/png"] = [".png"], ["image/webp"] = [".webp"],
    };

    // Cover photo of a ZEV. Changing it is ZEV administration: system administrator or the organization's
    // administrator; everyone who can read the ZEV can load it. 403 for readers, 404 for anyone else.
    public static RouteGroupBuilder MapZevImageEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/zevs/{zevId:guid}/image", async (Guid zevId, UploadZevImageRequest request, TenantAccess access,
            ZevDbContext database, IFileStorage storage, CancellationToken cancellationToken) =>
        {
            var zev = await database.Zevs.SingleOrDefaultAsync(item => item.Id == zevId, cancellationToken);
            if (await DeniedAsync(zev, access, cancellationToken) is { } denied) return denied;
            if (Validate(request) is not { } content)
                return Results.BadRequest(new { error = "Dozvoljene su slike JPG, PNG ili WEBP do 5 MB." });
            var key = await storage.SaveAsync(content, request.ContentType, cancellationToken);
            var previous = zev!.ImageStorageKey;
            zev.ImageStorageKey = key;
            zev.ImageContentType = request.ContentType;
            zev.ImageUrl = $"/api/zevs/{zev.Id}/image?v={key[..12]}";
            try { await database.SaveChangesAsync(cancellationToken); }
            catch
            {
                await storage.RemoveAsync(key, cancellationToken);
                throw;
            }
            if (previous is not null) await RemoveQuietlyAsync(storage, previous);
            return Results.Ok(new { zev.Id, zev.ImageUrl });
        }).RequireRateLimiting("upload");

        api.MapDelete("/zevs/{zevId:guid}/image", async (Guid zevId, TenantAccess access, ZevDbContext database,
            IFileStorage storage, CancellationToken cancellationToken) =>
        {
            var zev = await database.Zevs.SingleOrDefaultAsync(item => item.Id == zevId, cancellationToken);
            if (await DeniedAsync(zev, access, cancellationToken) is { } denied) return denied;
            if (zev!.ImageStorageKey is not { } key) return Results.NoContent();
            zev.ImageUrl = null;
            zev.ImageStorageKey = null;
            zev.ImageContentType = null;
            await database.SaveChangesAsync(cancellationToken);
            await RemoveQuietlyAsync(storage, key);
            return Results.NoContent();
        });

        api.MapGet("/zevs/{zevId:guid}/image", async (Guid zevId, TenantAccess access, ZevDbContext database,
            IFileStorage storage, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!await access.CanReadZevAsync(zevId, cancellationToken)) return Results.NotFound();
            var image = await database.Zevs.AsNoTracking().Where(item => item.Id == zevId)
                .Select(item => new { item.ImageStorageKey, item.ImageContentType }).SingleOrDefaultAsync(cancellationToken);
            if (image?.ImageStorageKey is null || image.ImageContentType is null) return Results.NotFound();
            byte[] content;
            try { content = await storage.ReadAsync(image.ImageStorageKey, cancellationToken); }
            catch (FileNotFoundException) { return Results.NotFound(); }
            // The URL changes with every new image (?v=), so the browser may keep this one.
            context.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(content, image.ImageContentType, lastModified: null, entityTag: null,
                enableRangeProcessing: false);
        });
        return api;
    }

    private static async Task<IResult?> DeniedAsync(ZevEntity? zev, TenantAccess access, CancellationToken cancellationToken)
    {
        if (zev is null) return Results.NotFound();
        if (await access.CanReadOrganizationAsync(zev.OrganizationId, cancellationToken)) return null;
        return await access.CanReadZevAsync(zev.Id, cancellationToken) ? Results.Forbid() : Results.NotFound();
    }

    // Name, declared type and the file's own signature must agree (a renamed PDF is not a photo).
    public static byte[]? Validate(UploadZevImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) || request.FileName != Path.GetFileName(request.FileName)
            || request.FileName.Length > 255 || !Extensions.TryGetValue(request.ContentType ?? "", out var extensions)
            || !extensions.Contains(Path.GetExtension(request.FileName).ToLowerInvariant())
            || string.IsNullOrWhiteSpace(request.ContentBase64) || request.ContentBase64.Length > (MaxBytes + 2) / 3 * 4 + 4)
            return null;
        byte[] content;
        try { content = Convert.FromBase64String(request.ContentBase64); }
        catch (FormatException) { return null; }
        return content.Length <= MaxBytes && LocalFileStorage.ValidContent(content, request.ContentType!) ? content : null;
    }

    // The new image is already saved; a file left behind must not fail the request.
    private static async Task RemoveQuietlyAsync(IFileStorage storage, string key)
    {
        try { await storage.RemoveAsync(key, CancellationToken.None); }
        catch (IOException) { }
        catch (InvalidDataException) { }
    }
}
