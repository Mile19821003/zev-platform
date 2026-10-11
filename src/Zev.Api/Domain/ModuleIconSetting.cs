namespace Zev.Api.Domain;

// Icon shown for a workspace module or category (navigation, dashboard). Keys of the icons are the palette of
// web/src/iconRegistry.ts; web/tests/moduleIcons.test.mjs checks that both lists stay the same.
public sealed class ModuleIconSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Module key of the web navigation ("payments", "lookups", …) or "category:<id>" for the four categories.
    public string ModuleKey { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = ModuleIcons.SeededUtc;
    public Guid? UpdatedByUserId { get; set; }
}

public static class ModuleIcons
{
    public static readonly DateTimeOffset SeededUtc = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    public static readonly string[] IconKeys =
    [
        "home", "cog", "sliders", "barcode", "list", "clipboard-check", "monitor", "package", "user", "users",
        "hard-hat", "activity", "shield-check", "bot", "file-lock", "file-text", "building", "building-2", "door", "key",
        "banknote", "wallet", "credit-card", "landmark", "receipt", "arrow-left-right", "chart-bar", "chart-pie", "scale",
        "scroll", "wrench", "megaphone", "vote", "link", "workflow", "network", "calendar", "bell", "palette", "droplet",
    ];

    // Initial icons: the ones the navigation used before icons became configurable.
    public static readonly (string ModuleKey, string IconKey)[] Defaults =
    [
        ("overview", "home"),
        ("category:finance", "banknote"), ("category:registry", "building-2"), ("category:community", "users"),
        ("category:admin", "shield-check"),
        ("finance", "file-text"), ("owner-finance", "wallet"), ("payments", "landmark"), ("bank-transactions", "arrow-left-right"),
        ("expenses", "receipt"), ("reports", "chart-bar"), ("legal", "scale"),
        ("zevs", "building-2"), ("buildings", "building"), ("entrances", "door"), ("units", "door"), ("people", "user"),
        ("ownership", "scroll"), ("registry", "building"),
        ("maintenance", "wrench"), ("representative-maintenance", "wrench"), ("my-maintenance", "wrench"),
        ("community", "megaphone"), ("representative-community", "megaphone"), ("owner-community", "vote"),
        ("access", "key"), ("memberships", "key"), ("users", "users"), ("accountLinks", "link"), ("organizations", "network"),
        ("hodogram", "workflow"), ("settings", "sliders"), ("lookups", "barcode"), ("icons", "palette"),
        ("errors", "activity"), ("versions", "clipboard-check"), ("deleted", "package"), ("themes", "monitor"),
    ];

    public static ModuleIconSetting[] Seed => Defaults.Select((row, index) => new ModuleIconSetting
    {
        Id = new Guid($"1c0e0000-0000-4000-8000-{index + 1:000000000000}"), ModuleKey = row.ModuleKey,
        IconKey = row.IconKey, UpdatedAt = SeededUtc
    }).ToArray();
}
