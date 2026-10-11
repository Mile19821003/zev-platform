namespace Zev.Api.Domain;

public enum SystemErrorSource { Frontend, Backend }
public enum SystemErrorStatus { Active, Resolved }

// Unhandled API exceptions and errors reported by the web app, for administrators to review and close.
public sealed class SystemErrorLog
{
    public const int TitleLength = 200, ErrorTypeLength = 200, MessageLength = 2000, StackTraceLength = 20000,
        PathLength = 300, NotesLength = 2000;

    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public SystemErrorSource Source { get; set; }
    public string ErrorType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    // Stack trace (backend) or the reported payload / component stack (frontend).
    public string? StackTrace { get; set; }
    public string? RequestPath { get; set; }
    public Guid? UserId { get; set; }
    public SystemErrorStatus Status { get; set; } = SystemErrorStatus.Active;
    public string? AdminNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    public static string Clip(string? value, int length) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= length ? value : value[..(length - 1)] + "…";
}

// Installed application versions (one row per release, matching CHANGELOG.md) with an administrator note.
public sealed class AppVersionHistory
{
    public const int NotesLength = 2000;

    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = "installed";
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
    public string InstalledBy { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public DateTimeOffset? NotesUpdatedAt { get; set; }
    public Guid? NotesUpdatedByUserId { get; set; }

    // "v2.10" → (2, 10) so versions sort numerically, not as text.
    public static (int Major, int Minor, int Patch) Parse(string version)
    {
        var parts = version.TrimStart('v', 'V').Split('.').Select(part => int.TryParse(part, out var number) ? number : 0).ToArray();
        return (parts.ElementAtOrDefault(0), parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2));
    }
}

public static class AppVersions
{
    public const string InstalledBy = "Migracija baze";
    public const string StartupInstalledBy = "Pokretanje aplikacije";

    // The running release (top entry of CHANGELOG.md, checked by a web test). At startup AppVersionRecorder adds it
    // to the history when it is not there yet, so a new release only changes these two constants.
    public const string Current = "v2.20";
    public const string CurrentTitle = "Održavanje: prijave stanara, obavijesti, radni nalozi i izvještaji";

    // Releases v2.7–v2.16, recorded by migrations before the startup recording existed.
    public static readonly AppVersionHistory[] Seed =
    [
        Row("v2.7", "Navigacija radnog prostora: kontrolna ploča i sklopivi meni", 2026, 10, 10),
        Row("v2.8", "Sistemska podešavanja i regionalni prikaz iznosa i datuma", 2026, 10, 10),
        Row("v2.9", "Šifarnici (generičke klasifikacije i padajuće liste)", 2026, 10, 10),
        Row("v2.10", "Prikazi i ikone: paleta ikonica za module i kategorije", 2026, 10, 10),
        Row("v2.11", "Šifarnici u formama (useLookup, storno ručne uplate)", 2026, 10, 11),
        Row("v2.12", "Greške u sistemu, istorija verzija i kategorije dokumenata", 2026, 10, 11),
        Row("v2.13", "Obrisani redovi: soft-delete i vraćanje zapisa (Recycle Bin)", 2026, 10, 11),
        Row("v2.14", "Teme i paleta boja (CSS varijable, pregled uživo)", 2026, 10, 11),
        Row("v2.15", "Fotografija zgrade na karticama zajednica", 2026, 10, 11),
        Row("v2.16", "Kalendar održavanja: statusi Planirano / Dospjelo / Završeno, horizont 60 dana", 2026, 10, 11),
    ];

    private static AppVersionHistory Row(string version, string title, int year, int month, int day) => new()
    {
        Version = version, Title = title, Status = "installed", InstalledBy = InstalledBy,
        InstalledAt = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero)
    };
}
