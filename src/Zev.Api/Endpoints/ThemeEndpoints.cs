using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Endpoints;

public sealed record UpdateThemeRequest(string? ActiveThemeName, string? PrimaryColor, string? SecondaryColor,
    string? AccentColor, string? BackgroundColor, string? SurfaceColor, string? TextColor, string? HeaderBgColor,
    string? HeaderTextColor, string? MenuBgColor, string? MenuTextColor, string? MenuActiveColor, string? BorderColor,
    DateTimeOffset? UpdatedAt)
{
    public string?[] Colors() => [PrimaryColor, SecondaryColor, AccentColor, BackgroundColor, SurfaceColor, TextColor,
        HeaderBgColor, HeaderTextColor, MenuBgColor, MenuTextColor, MenuActiveColor, BorderColor];
}

public sealed record ResetThemeRequest(DateTimeOffset? UpdatedAt);

public static class ThemeEndpoints
{
    // Colours are read before sign-in too (the login page uses them); changing them is for system administrators.
    public static RouteGroupBuilder MapThemeEndpoint(this RouteGroupBuilder api)
    {
        api.MapGet("/system/theme", async (ZevDbContext database, CancellationToken cancellationToken) =>
            Results.Ok(View(await database.ThemeSettings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == SystemThemeSettings.SingletonId,
                cancellationToken) ?? SystemThemeSettings.Factory()))).AllowAnonymous();
        return api;
    }

    // The /system group requires the SystemAdministrator policy.
    public static RouteGroupBuilder MapThemeAdminEndpoints(this RouteGroupBuilder system)
    {
        system.MapPut("/theme", async (UpdateThemeRequest request, ZevDbContext database, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var colors = request.Colors().Select(SystemThemeSettings.NormalizeColor).ToArray();
            if (colors.Any(color => color is null))
                return Results.BadRequest(new { error = "Boje moraju biti u obliku #RRGGBB." });
            if (!SystemThemeSettings.ThemeNames.Contains(request.ActiveThemeName ?? ""))
                return Results.BadRequest(new { error = "Nepoznata tema." });
            return await SaveAsync(database, context, request.UpdatedAt, "ThemeUpdated", theme =>
            {
                theme.ActiveThemeName = request.ActiveThemeName!;
                theme.SetColors(colors!);
            }, cancellationToken);
        });

        system.MapPost("/theme/reset", (ResetThemeRequest request, ZevDbContext database, HttpContext context,
            CancellationToken cancellationToken) =>
            SaveAsync(database, context, request.UpdatedAt, "ThemeReset", theme =>
            {
                var factory = SystemThemeSettings.Factory();
                theme.ActiveThemeName = factory.ActiveThemeName;
                theme.SetColors(factory.Colors());
            }, cancellationToken));
        return system;
    }

    private static async Task<IResult> SaveAsync(ZevDbContext database, HttpContext context, DateTimeOffset? seen,
        string action, Action<SystemThemeSettings> change, CancellationToken cancellationToken)
    {
        var userId = Guid.TryParse(context.User.FindFirst("sub")?.Value, out var parsed) ? parsed : (Guid?)null;
        var theme = await database.ThemeSettings.SingleOrDefaultAsync(item => item.Id == SystemThemeSettings.SingletonId, cancellationToken);
        if (theme is null)
        {
            theme = SystemThemeSettings.Factory();
            database.ThemeSettings.Add(theme);
        }
        // Optimistic check: the caller must have seen the current version.
        else if (seen != theme.UpdatedAt)
            return Results.Conflict(new { error = "Tema je u međuvremenu izmijenjena. Osvježite i pokušajte ponovo." });
        var before = JsonSerializer.Serialize(View(theme));
        change(theme);
        theme.UpdatedAt = DateTimeOffset.UtcNow;
        theme.UpdatedByUserId = userId;
        database.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId, EntityType = nameof(SystemThemeSettings), EntityId = SystemThemeSettings.SingletonId.ToString(),
            Action = action, BeforeJson = before, AfterJson = JsonSerializer.Serialize(View(theme)),
            CorrelationId = context.TraceIdentifier
        });
        await database.SaveChangesAsync(cancellationToken);
        return Results.Ok(View(theme));
    }

    private static object View(SystemThemeSettings theme) => new
    {
        theme.ActiveThemeName, theme.PrimaryColor, theme.SecondaryColor, theme.AccentColor, theme.BackgroundColor,
        theme.SurfaceColor, theme.TextColor, theme.HeaderBgColor, theme.HeaderTextColor, theme.MenuBgColor,
        theme.MenuTextColor, theme.MenuActiveColor, theme.BorderColor, theme.UpdatedAt,
        presets = SystemThemeSettings.Presets.Select(preset => new
        {
            preset.Name, preset.Label,
            colors = SystemThemeSettings.ColorNames.Select((name, index) => (name, value: preset.Colors[index]))
                .ToDictionary(item => char.ToLowerInvariant(item.name[0]) + item.name[1..], item => item.value)
        })
    };
}
