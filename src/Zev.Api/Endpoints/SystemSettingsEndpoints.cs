using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class SystemSettingsEndpoints
{
    // GET for every signed-in user (the clients format with it); PUT for system administrators only.
    // There is no per-ZEV permission for this: the settings are global, so ZEV roles cannot change them.
    public static RouteGroupBuilder MapSystemSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/system/settings", async (ZevDbContext database, CancellationToken cancellationToken) =>
            Results.Ok(View(await ReadAsync(database, cancellationToken))));

        api.MapPut("/system/settings", async (UpdateSystemSettingsRequest request, TenantAccess access,
            ZevDbContext database, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid userId) return Results.Unauthorized();
            if (!await access.IsSystemAdministratorAsync(userId, cancellationToken)) return Results.Forbid();
            if (!SystemSettings.DecimalSeparators.Contains(request.DecimalSeparator)
                || !SystemSettings.DateFormats.Contains(request.DateFormat)
                || !SystemSettings.FontSizes.Contains(request.FontSize)
                || !SystemSettings.Themes.Contains(request.DefaultTheme ?? "Default"))
                return Results.BadRequest(new { error = "Nedozvoljena vrijednost podešavanja." });
            var settings = await database.SystemSettings.SingleOrDefaultAsync(item => item.Id == SystemSettings.SingletonId,
                cancellationToken);
            if (settings is null)
            {
                settings = new SystemSettings();
                database.SystemSettings.Add(settings);
            }
            // Optimistic check: the caller must have seen the current version.
            else if (request.UpdatedAt is not DateTimeOffset seen || seen != settings.UpdatedAt)
                return Results.Conflict(new { error = "Podešavanja su u međuvremenu izmijenjena. Osvježite i pokušajte ponovo." });
            var before = JsonSerializer.Serialize(View(settings));
            settings.DecimalSeparator = request.DecimalSeparator;
            settings.DateFormat = request.DateFormat;
            settings.FontSize = request.FontSize;
            settings.DefaultTheme = request.DefaultTheme ?? "Default";
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            settings.UpdatedByUserId = userId;
            database.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId, EntityType = nameof(SystemSettings), EntityId = SystemSettings.SingletonId.ToString(),
                Action = "SystemSettingsUpdated", BeforeJson = before, AfterJson = JsonSerializer.Serialize(View(settings)),
                CorrelationId = context.TraceIdentifier
            });
            await database.SaveChangesAsync(cancellationToken);
            return Results.Ok(View(settings));
        });
        return api;
    }

    // A database without the seeded row (e.g. a fresh test database) still answers with the defaults.
    private static async Task<SystemSettings> ReadAsync(ZevDbContext database, CancellationToken cancellationToken) =>
        await database.SystemSettings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == SystemSettings.SingletonId,
            cancellationToken) ?? new SystemSettings();

    private static object View(SystemSettings settings) => new
    {
        settings.DecimalSeparator, settings.DateFormat, settings.FontSize, settings.DefaultTheme,
        settings.UpdatedAt, settings.UpdatedByUserId,
        options = new
        {
            decimalSeparators = SystemSettings.DecimalSeparators, dateFormats = SystemSettings.DateFormats,
            fontSizes = SystemSettings.FontSizes, themes = SystemSettings.Themes
        }
    };
}

public sealed record UpdateSystemSettingsRequest(string DecimalSeparator, string DateFormat, string FontSize,
    string? DefaultTheme, DateTimeOffset? UpdatedAt);
