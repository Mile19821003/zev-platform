using Microsoft.Extensions.Configuration;

namespace Zev.Api.Payments;

public sealed record PaymentAccountReference(Guid ZevId, Guid PropertyUnitId, Guid PersonId);

public interface IPaymentReferenceProvider
{
    string ForAccount(Guid zevId, Guid propertyUnitId, Guid personId);
    PaymentAccountReference? Parse(string? reference);
}

public sealed class PaymentReferenceProvider(IConfiguration? configuration = null) : IPaymentReferenceProvider
{
    private readonly string prefix = ReadPrefix(configuration);

    public string ForAccount(Guid zevId, Guid propertyUnitId, Guid personId) =>
        $"{prefix}-{zevId:N}-{propertyUnitId:N}-{personId:N}";

    public PaymentAccountReference? Parse(string? reference)
    {
        var first = prefix.Length + 1;
        if (reference is null || reference.Length != prefix.Length + 99
            || !reference.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)
            || reference[first + 32] != '-' || reference[first + 65] != '-') return null;
        return Guid.TryParseExact(reference.AsSpan(first, 32), "N", out var zevId)
            && Guid.TryParseExact(reference.AsSpan(first + 33, 32), "N", out var unitId)
            && Guid.TryParseExact(reference.AsSpan(first + 66, 32), "N", out var personId)
            ? new(zevId, unitId, personId) : null;
    }

    private static string ReadPrefix(IConfiguration? configuration)
    {
        var value = configuration?["Finance:PaymentReferencePrefix"]?.Trim().ToUpperInvariant() ?? "ZEV";
        if (value.Length is < 2 or > 10 || !value.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("Finance:PaymentReferencePrefix must contain 2-10 ASCII letters/digits.");
        return value;
    }
}
