using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class ModuleIconEndpoints
{
    // GET for every signed-in user (the navigation renders with it); PUT for system administrators only.
    public static RouteGroupBuilder MapModuleIconEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/system/module-icons", async (ZevDbContext database, CancellationToken cancellationToken) =>
            Results.Ok(await ViewAsync(database, cancellationToken)));

        api.MapPut("/system/module-icons", async (UpdateModuleIconsRequest request, TenantAccess access,
            ZevDbContext database, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid userId) return Results.Unauthorized();
            if (!await access.IsSystemAdministratorAsync(userId, cancellationToken)) return Results.Forbid();
            var changes = request.Changes ?? [];
            if (changes.Count is 0 or > 200 || changes.Select(item => item.ModuleKey).Distinct().Count() != changes.Count)
                return Results.BadRequest(new { error = "Pošaljite 1–200 različitih modula." });
            var settings = await database.ModuleIconSettings.ToDictionaryAsync(item => item.ModuleKey, cancellationToken);
            var unknownModule = changes.FirstOrDefault(item => item.ModuleKey is null || !settings.ContainsKey(item.ModuleKey));
            if (unknownModule is not null) return Results.BadRequest(new { error = $"Nepoznat modul '{unknownModule.ModuleKey}'." });
            var unknownIcon = changes.FirstOrDefault(item => !ModuleIcons.IconKeys.Contains(item.IconKey));
            if (unknownIcon is not null) return Results.BadRequest(new { error = $"Ikonica '{unknownIcon.IconKey}' nije u paleti." });
            var now = DateTimeOffset.UtcNow;
            var changed = changes.Where(item => settings[item.ModuleKey!].IconKey != item.IconKey).ToList();
            if (changed.Count > 0)
            {
                database.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = userId, EntityType = nameof(ModuleIconSetting), EntityId = "module-icons",
                    Action = "ModuleIconsUpdated",
                    BeforeJson = JsonSerializer.Serialize(changed.ToDictionary(item => item.ModuleKey!, item => settings[item.ModuleKey!].IconKey)),
                    AfterJson = JsonSerializer.Serialize(changed.ToDictionary(item => item.ModuleKey!, item => item.IconKey)),
                    CorrelationId = context.TraceIdentifier
                });
                foreach (var item in changed)
                {
                    var setting = settings[item.ModuleKey!];
                    setting.IconKey = item.IconKey!;
                    setting.UpdatedAt = now;
                    setting.UpdatedByUserId = userId;
                }
                await database.SaveChangesAsync(cancellationToken);
            }
            return Results.Ok(await ViewAsync(database, cancellationToken));
        });
        return api;
    }

    private static async Task<object> ViewAsync(ZevDbContext database, CancellationToken cancellationToken)
    {
        var rows = await database.ModuleIconSettings.AsNoTracking().OrderBy(item => item.ModuleKey)
            .Select(item => new { item.ModuleKey, item.IconKey, item.UpdatedAt }).ToListAsync(cancellationToken);
        return new
        {
            icons = rows.ToDictionary(item => item.ModuleKey, item => item.IconKey),
            updatedAt = rows.Count == 0 ? ModuleIcons.SeededUtc : rows.Max(item => item.UpdatedAt),
            palette = ModuleIcons.IconKeys
        };
    }
}

public sealed record ModuleIconChange(string? ModuleKey, string? IconKey);
public sealed record UpdateModuleIconsRequest(List<ModuleIconChange>? Changes);
