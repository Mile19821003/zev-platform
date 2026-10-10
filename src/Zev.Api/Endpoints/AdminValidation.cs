namespace Zev.Api.Endpoints;

internal static class AdminValidation
{
    public static (int Page, int PageSize) Page(int? page, int? pageSize) =>
        (Math.Clamp(page ?? 1, 1, 100000), Math.Clamp(pageSize ?? 20, 1, 100));

    public static bool Code(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 50
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static bool Required(string? value, int maxLength) => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= maxLength;

    public static bool Optional(string? value, int maxLength) => value is null || value.Trim().Length <= maxLength;

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // BiH transaction account: exactly 16 digits. Spaces and hyphens used for readability are removed.
    public static string? TransactionAccount(string? value)
    {
        var digits = value?.Replace(" ", string.Empty).Replace("-", string.Empty);
        return digits is { Length: 16 } && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    public static bool TryRowVersion(string? value, out byte[] rowVersion)
    {
        rowVersion = [];
        if (value is null)
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
