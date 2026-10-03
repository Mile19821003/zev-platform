using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;
using Zev.Api.Storage;

namespace Zev.Api.Endpoints;

public static class CommunityEndpoints
{
    public static RouteGroupBuilder MapCommunityEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/community");

        group.MapGet("/announcements", async (Guid zevId, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Community.View", cancellationToken, staffOnly: false)
                || access.CurrentUserId is not Guid userId)
                return Results.NotFound();
            var staff = await access.HasPermissionAsync(zevId, "Community.View", cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Announcements.AsNoTracking().Where(item => item.ZevId == zevId);
            if (!staff) query = query.Where(item => item.PublishedFromUtc <= now
                && (item.ExpiresUtc == null || item.ExpiresUtc > now)
                && (item.Scope == AudienceScope.Zev
                    || (item.Scope == AudienceScope.Role && item.TargetRole != MembershipRole.Owner
                        && database.Memberships.Any(membership =>
                        membership.UserId == userId && membership.IsActive && membership.ZevId == zevId
                        && membership.Role == item.TargetRole)
                        || (item.Scope == AudienceScope.Role && item.TargetRole == MembershipRole.Owner
                            && database.Ownerships.Any(ownership => ownership.ZevId == zevId
                                && ownership.Person.UserId == userId && ownership.Person.IsActive
                                && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                        && ownership.PropertyUnit.IsActive
                        && ownership.ValidFrom <= today && (ownership.ValidTo == null || ownership.ValidTo > today))))
                    || (item.Scope == AudienceScope.Building && database.Ownerships.Any(ownership =>
                        ownership.PropertyUnit.BuildingId == item.BuildingId && ownership.Person.UserId == userId
                        && ownership.Person.IsActive && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                        && ownership.PropertyUnit.IsActive
                        && ownership.ValidFrom <= today && (ownership.ValidTo == null || ownership.ValidTo > today)))
                    || (item.Scope == AudienceScope.Entrance && database.Ownerships.Any(ownership =>
                        ownership.PropertyUnit.EntranceId == item.EntranceId && ownership.Person.UserId == userId
                        && ownership.Person.IsActive && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                        && ownership.PropertyUnit.IsActive
                        && ownership.ValidFrom <= today && (ownership.ValidTo == null || ownership.ValidTo > today)))));
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.PublishedFromUtc)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.Title, item.Content, scope = item.Scope.ToString(),
                    item.BuildingId, item.EntranceId, targetRole = item.TargetRole.ToString(),
                    item.PublishedFromUtc, item.ExpiresUtc, item.PublishedByUserId })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("/announcements", async (Guid zevId, CreateAnnouncementRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Announcements.Create", cancellationToken)
                || access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (!AdminValidation.Required(request.Title, 200) || !AdminValidation.Required(request.Content, 10000)
                || !Enum.TryParse<AudienceScope>(request.Scope, true, out var scope) || !Enum.IsDefined(scope)
                || request.ExpiresUtc <= request.PublishedFromUtc) return Results.BadRequest();
            MembershipRole? role = null;
            if (scope == AudienceScope.Role)
            {
                if (!Enum.TryParse<MembershipRole>(request.TargetRole, true, out var selectedRole)
                    || !Enum.IsDefined(selectedRole)) return Results.BadRequest();
                role = selectedRole;
            }
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            if ((scope == AudienceScope.Building && (request.BuildingId is not Guid buildingId
                    || !await database.Buildings.AnyAsync(item => item.Id == buildingId && item.ZevId == zevId, cancellationToken)))
                || (scope == AudienceScope.Entrance && (request.EntranceId is not Guid entranceId
                    || request.BuildingId is not Guid selectedBuilding
                    || !await database.Entrances.AnyAsync(item => item.Id == entranceId && item.BuildingId == selectedBuilding
                        && item.ZevId == zevId, cancellationToken)))) return Results.BadRequest();
            var fileIds = request.FileIds ?? [];
            if (fileIds.Count > 10 || fileIds.Distinct().Count() != fileIds.Count
                || await database.StoredFiles.CountAsync(item => fileIds.Contains(item.Id) && item.ZevId == zevId
                    && item.UploadedByUserId == userId, cancellationToken) != fileIds.Count) return Results.BadRequest();
            var announcement = new Announcement
            {
                OrganizationId = organizationId, ZevId = zevId, PublishedByUserId = userId,
                Scope = scope, BuildingId = scope is AudienceScope.Building or AudienceScope.Entrance ? request.BuildingId : null,
                EntranceId = scope == AudienceScope.Entrance ? request.EntranceId : null,
                TargetRole = role, Title = request.Title.Trim(), Content = request.Content.Trim(),
                PublishedFromUtc = request.PublishedFromUtc, ExpiresUtc = request.ExpiresUtc
            };
            database.Announcements.Add(announcement);
            database.AnnouncementFiles.AddRange(fileIds.Select(fileId => new AnnouncementFile
            {
                OrganizationId = organizationId, ZevId = zevId, AnnouncementId = announcement.Id, StoredFileId = fileId
            }));
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/community/announcements/{announcement.Id}", new { announcement.Id });
        });

        group.MapGet("/documents", async (Guid zevId, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Documents.View", cancellationToken, staffOnly: false)
                || access.CurrentUserId is not Guid userId)
                return Results.NotFound();
            var manager = await access.CanManageLegalCasesAsync(zevId, cancellationToken)
                && await access.HasPermissionAsync(zevId, "Documents.View", cancellationToken);
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = VisibleDocuments(database, zevId, userId, manager);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.CreatedUtc)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.Title, type = item.Type.ToString(),
                    visibility = item.Visibility.ToString(), item.PersonId, item.PropertyUnitId,
                    targetRole = item.TargetRole.ToString(), item.StoredFileId, item.CreatedUtc })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("/documents", async (Guid zevId, CreateManagedDocumentRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Documents.Upload", cancellationToken)
                || access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (!AdminValidation.Required(request.Title, 200)
                || !Enum.TryParse<ManagedDocumentType>(request.Type, true, out var type) || !Enum.IsDefined(type)
                || !Enum.TryParse<DocumentVisibility>(request.Visibility, true, out var visibility)
                || !Enum.IsDefined(visibility)
                || (visibility == DocumentVisibility.PersonOnly) != (request.PersonId is not null)
                || (visibility == DocumentVisibility.PropertyUnit) != (request.PropertyUnitId is not null)
                || (visibility == DocumentVisibility.Role) != (request.TargetRole is not null)
                || (type is ManagedDocumentType.Financial or ManagedDocumentType.Legal
                    && visibility == DocumentVisibility.ZevMembers)) return Results.BadRequest();
            MembershipRole? targetRole = null;
            if (request.TargetRole is not null)
            {
                if (!Enum.TryParse<MembershipRole>(request.TargetRole, true, out var parsedRole)
                    || !Enum.IsDefined(parsedRole)) return Results.BadRequest();
                targetRole = parsedRole;
            }
            var file = await database.StoredFiles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.FileId
                && item.ZevId == zevId, cancellationToken);
            if (file is null || (request.PersonId is Guid personId && !await database.People.AnyAsync(item =>
                item.Id == personId && item.OrganizationId == file.OrganizationId, cancellationToken))
                || (request.PropertyUnitId is Guid unitId && !await database.PropertyUnits.AnyAsync(item =>
                    item.Id == unitId && item.ZevId == zevId, cancellationToken)))
                return Results.NotFound();
            var document = new ManagedDocument { OrganizationId = file.OrganizationId, ZevId = zevId,
                StoredFileId = file.Id, UploadedByUserId = userId, Title = request.Title.Trim(),
                Type = type, Visibility = visibility, PersonId = request.PersonId,
                PropertyUnitId = request.PropertyUnitId, TargetRole = targetRole };
            database.ManagedDocuments.Add(document);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/community/documents/{document.Id}", new { document.Id });
        });

        group.MapPatch("/documents/{documentId:guid}/visibility", async (Guid zevId, Guid documentId,
            UpdateManagedDocumentVisibilityRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Documents.Edit", cancellationToken)
                || access.CurrentUserId is not Guid actorId) return Results.NotFound();
            if (!Enum.TryParse<DocumentVisibility>(request.Visibility, true, out var visibility)
                || !Enum.IsDefined(visibility)
                || (visibility == DocumentVisibility.PersonOnly) != (request.PersonId is not null)
                || (visibility == DocumentVisibility.PropertyUnit) != (request.PropertyUnitId is not null)
                || (visibility == DocumentVisibility.Role) != (request.TargetRole is not null)
                || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
                return Results.BadRequest();
            MembershipRole? targetRole = null;
            if (request.TargetRole is not null)
            {
                if (!Enum.TryParse<MembershipRole>(request.TargetRole, true, out var role)
                    || !Enum.IsDefined(role)) return Results.BadRequest();
                targetRole = role;
            }
            var document = await database.ManagedDocuments.SingleOrDefaultAsync(item => item.Id == documentId
                && item.ZevId == zevId, cancellationToken);
            if (document is null) return Results.NotFound();
            if (document.Type is ManagedDocumentType.Financial or ManagedDocumentType.Legal
                && visibility == DocumentVisibility.ZevMembers) return Results.BadRequest();
            if (request.PersonId is Guid personId && !await database.People.AnyAsync(item => item.Id == personId
                && item.OrganizationId == document.OrganizationId, cancellationToken)) return Results.NotFound();
            if (request.PropertyUnitId is Guid unitId && !await database.PropertyUnits.AnyAsync(item => item.Id == unitId
                && item.ZevId == zevId, cancellationToken)) return Results.NotFound();
            var before = new { document.Visibility, document.PersonId, document.PropertyUnitId, document.TargetRole };
            document.Visibility = visibility;
            document.PersonId = request.PersonId;
            document.PropertyUnitId = request.PropertyUnitId;
            document.TargetRole = targetRole;
            database.AuditLogs.Add(new AuditLog { ActorUserId = actorId, OrganizationId = document.OrganizationId,
                ZevId = zevId, EntityType = nameof(ManagedDocument), EntityId = document.Id.ToString(),
                Action = "Visibility", BeforeJson = System.Text.Json.JsonSerializer.Serialize(before),
                AfterJson = System.Text.Json.JsonSerializer.Serialize(new { visibility, request.PersonId,
                    request.PropertyUnitId, targetRole, Reason = request.Reason.Trim() }) });
            await database.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/documents/{documentId:guid}/download", async (Guid zevId, Guid documentId,
            TenantAccess access, ZevDbContext database, IFileStorage storage, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Documents.View", cancellationToken, staffOnly: false)
                || !await access.HasPermissionAsync(zevId, "Documents.Download", cancellationToken, staffOnly: false)
                || access.CurrentUserId is not Guid userId)
                return Results.NotFound();
            var manager = await access.CanManageLegalCasesAsync(zevId, cancellationToken)
                && await access.HasPermissionAsync(zevId, "Documents.Download", cancellationToken);
            var document = await VisibleDocuments(database, zevId, userId, manager)
                .Where(item => item.Id == documentId).Select(item => new { item.StoredFileId,
                    item.OrganizationId, item.Visibility })
                .SingleOrDefaultAsync(cancellationToken);
            if (document is null) return Results.NotFound();
            var file = await database.StoredFiles.AsNoTracking().SingleAsync(item => item.Id == document.StoredFileId,
                cancellationToken);
            var content = await storage.ReadAsync(file.StorageKey, cancellationToken);
            if (document.Visibility != DocumentVisibility.ZevMembers)
            {
                database.AuditLogs.Add(new AuditLog { ActorUserId = userId, OrganizationId = document.OrganizationId,
                    ZevId = zevId, EntityType = nameof(ManagedDocument), EntityId = documentId.ToString(),
                    Action = "Download", AfterJson = System.Text.Json.JsonSerializer.Serialize(new { document.Visibility }) });
                await database.SaveChangesAsync(cancellationToken);
            }
            return Results.File(content, file.ContentType,
                file.OriginalFileName);
        });

        return api;
    }

    private static IQueryable<ManagedDocument> VisibleDocuments(ZevDbContext database, Guid zevId,
        Guid userId, bool manager)
    {
        var query = database.ManagedDocuments.AsNoTracking().Where(item => item.ZevId == zevId);
        if (!manager)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            query = query.Where(item => item.Visibility == DocumentVisibility.ZevMembers
                || (item.Visibility == DocumentVisibility.PersonOnly && database.Ownerships.Any(ownership =>
                    ownership.ZevId == zevId && ownership.PersonId == item.PersonId
                    && ownership.Person.UserId == userId && ownership.Person.IsActive
                    && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                    && ownership.PropertyUnit.IsActive
                    && ownership.ValidFrom <= today && (ownership.ValidTo == null || ownership.ValidTo > today)))
                || (item.Visibility == DocumentVisibility.PropertyUnit && database.Ownerships.Any(ownership =>
                    ownership.ZevId == zevId && ownership.PropertyUnitId == item.PropertyUnitId
                    && ownership.Person.UserId == userId && ownership.Person.IsActive
                    && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                    && ownership.PropertyUnit.IsActive
                    && ownership.ValidFrom <= today && (ownership.ValidTo == null || ownership.ValidTo > today)))
                || (item.Visibility == DocumentVisibility.Role
                    && ((item.TargetRole == MembershipRole.Owner && database.Ownerships.Any(ownership =>
                        ownership.ZevId == zevId && ownership.Person.UserId == userId && ownership.Person.IsActive
                        && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                        && ownership.PropertyUnit.IsActive
                        && ownership.ValidFrom <= today && (ownership.ValidTo == null || ownership.ValidTo > today)))
                    || (item.TargetRole != MembershipRole.Owner && database.Memberships.Any(membership =>
                        membership.UserId == userId && membership.IsActive
                        && membership.ZevId == zevId && membership.Role == item.TargetRole)))));
        }
        return query;
    }
}

public sealed record CreateAnnouncementRequest(string Title, string Content, string Scope, Guid? BuildingId,
    Guid? EntranceId, string? TargetRole, DateTimeOffset PublishedFromUtc, DateTimeOffset? ExpiresUtc,
    List<Guid>? FileIds);
public sealed record CreateManagedDocumentRequest(string Title, string Type, string Visibility,
    Guid FileId, Guid? PersonId, Guid? PropertyUnitId = null, string? TargetRole = null);
public sealed record UpdateManagedDocumentVisibilityRequest(string Visibility, Guid? PersonId,
    Guid? PropertyUnitId, string? TargetRole, string Reason);
