namespace Zev.Api.Domain;

// Platform-wide display settings (one row). Formatting is applied by the clients, not by the API.
public sealed class SystemSettings
{
    public const int SingletonId = 1;
    public static readonly DateTimeOffset SeededUtc = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    public static readonly string[] DecimalSeparators = [",", "."];
    public static readonly string[] DateFormats = ["DD.MM.YYYY", "YYYY-MM-DD"];
    public static readonly string[] FontSizes = ["13px", "14px", "15px"];
    public static readonly string[] Themes = ["Default"];

    public int Id { get; set; } = SingletonId;
    public string DecimalSeparator { get; set; } = ",";
    public string DateFormat { get; set; } = "DD.MM.YYYY";
    public string FontSize { get; set; } = "13px";
    public string DefaultTheme { get; set; } = "Default";
    public DateTimeOffset UpdatedAt { get; set; } = SeededUtc;
    public Guid? UpdatedByUserId { get; set; }
}
