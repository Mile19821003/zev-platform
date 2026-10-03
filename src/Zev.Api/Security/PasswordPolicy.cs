namespace Zev.Api.Security;

public sealed class PasswordPolicy(IConfiguration configuration)
{
    public int MinimumLength => Math.Max(12, configuration.GetValue<int?>("PasswordPolicy:MinimumLength") ?? 12);
    public int MaximumLength => Math.Max(MinimumLength, configuration.GetValue<int?>("PasswordPolicy:MaximumLength") ?? 256);

    public string? Validate(string? password)
    {
        if (password is null || password.Length < MinimumLength)
            return $"Lozinka mora imati najmanje {MinimumLength} znakova.";
        if (password.Length > MaximumLength)
            return $"Lozinka može imati najviše {MaximumLength} znakova.";
        if (!password.Any(char.IsUpper))
            return "Lozinka mora sadržavati veliko slovo.";
        if (!password.Any(char.IsLower))
            return "Lozinka mora sadržavati malo slovo.";
        if (!password.Any(char.IsDigit))
            return "Lozinka mora sadržavati broj.";
        if (!password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)))
            return "Lozinka mora sadržavati specijalni znak.";
        return null;
    }
}
