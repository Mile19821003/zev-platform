using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Payments;

public sealed record PaymentAccountReference(Guid ZevId, Guid PropertyUnitId, Guid PersonId);

// Poziv na broj: 10-digit payment account number followed by 2 ISO 7064 MOD 97-10 check digits
// (12 digits, numeric only, fits domestic payment orders). Spaces and hyphens are ignored when parsing.
public static class PaymentReferenceFormat
{
    public const int Length = 12;
    public const long MaxNumber = 9_999_999_999;

    public static string Format(long number)
    {
        if (number is < 1 or > MaxNumber) throw new ArgumentOutOfRangeException(nameof(number));
        var body = number.ToString("D10", CultureInfo.InvariantCulture);
        return body + (98 - Mod97(body + "00")).ToString("D2", CultureInfo.InvariantCulture);
    }

    public static long? Parse(string? reference)
    {
        var digits = reference?.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (digits is not { Length: Length } || !digits.All(char.IsAsciiDigit) || Mod97(digits) != 1) return null;
        var number = long.Parse(digits.AsSpan(0, 10), CultureInfo.InvariantCulture);
        return number > 0 ? number : null;
    }

    private static int Mod97(string digits)
    {
        var remainder = 0;
        foreach (var digit in digits) remainder = (remainder * 10 + (digit - '0')) % 97;
        return remainder;
    }
}

// Account reference used before October 2026: PREFIX-{zevId:N}-{unitId:N}-{personId:N} (prefix + 99 characters).
// Issued invoices keep it as an immutable snapshot, so statements quoting it are still recognised.
public static class LegacyPaymentReference
{
    public static string Format(Guid zevId, Guid propertyUnitId, Guid personId, string prefix = "ZEV") =>
        $"{prefix}-{zevId:N}-{propertyUnitId:N}-{personId:N}";

    public static PaymentAccountReference? Parse(string? reference)
    {
        var parts = reference?.Trim().Split('-');
        if (parts is not { Length: 4 } || parts[0].Length is < 2 or > 10 || !parts[0].All(char.IsAsciiLetterOrDigit))
            return null;
        return Guid.TryParseExact(parts[1], "N", out var zevId) && Guid.TryParseExact(parts[2], "N", out var unitId)
            && Guid.TryParseExact(parts[3], "N", out var personId) ? new(zevId, unitId, personId) : null;
    }
}

public sealed class PaymentReferenceService(ZevDbContext database)
{
    public async Task<string> EnsureAsync(Guid zevId, Guid propertyUnitId, Guid personId,
        CancellationToken cancellationToken) =>
        (await EnsureAsync(zevId, [(propertyUnitId, personId)], cancellationToken))[(propertyUnitId, personId)];

    // Returns the reference of every (unit, person) pair, creating missing payment accounts. Saves only the
    // new accounts, so callers must not have other pending changes on the context.
    public async Task<IReadOnlyDictionary<(Guid PropertyUnitId, Guid PersonId), string>> EnsureAsync(Guid zevId,
        IReadOnlyCollection<(Guid PropertyUnitId, Guid PersonId)> pairs, CancellationToken cancellationToken)
    {
        var wanted = pairs.Distinct().ToList();
        for (var attempt = 0; ; attempt++)
        {
            var existing = await LoadAsync(zevId, wanted, cancellationToken);
            var missing = wanted.Where(pair => !existing.ContainsKey(pair)).ToList();
            if (missing.Count == 0)
                return existing.ToDictionary(item => item.Key, item => PaymentReferenceFormat.Format(item.Value));
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var added = missing.Select(pair => new PaymentAccount { OrganizationId = organizationId, ZevId = zevId,
                PropertyUnitId = pair.PropertyUnitId, PersonId = pair.PersonId }).ToList();
            database.PaymentAccounts.AddRange(added);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException problem) when (attempt == 0 && DbUpdateErrors.IsUniqueViolation(problem))
            {
                // A concurrent request created the same account first; drop ours and read theirs.
                foreach (var account in added) database.Entry(account).State = EntityState.Detached;
            }
        }
    }

    // Maps statement references (short numeric or legacy) to payment accounts of this ZEV.
    public async Task<IReadOnlyDictionary<string, PaymentAccountReference>> ResolveAsync(Guid zevId,
        IEnumerable<string?> references, CancellationToken cancellationToken)
    {
        var distinct = references.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).Distinct().ToList();
        var numbers = distinct.Select(item => (Reference: item, Number: PaymentReferenceFormat.Parse(item)))
            .Where(item => item.Number is not null).ToList();
        var wantedNumbers = numbers.Select(item => item.Number!.Value).Distinct().ToArray();
        var accounts = wantedNumbers.Length == 0 ? [] : await database.PaymentAccounts.AsNoTracking()
            .Where(item => item.ZevId == zevId && wantedNumbers.Contains(item.Number))
            .ToDictionaryAsync(item => item.Number, cancellationToken);
        var result = new Dictionary<string, PaymentAccountReference>(StringComparer.Ordinal);
        foreach (var (reference, number) in numbers)
            if (accounts.TryGetValue(number!.Value, out var account))
                result[reference] = new(account.ZevId, account.PropertyUnitId, account.PersonId);
        foreach (var reference in distinct.Where(item => !result.ContainsKey(item)))
            if (LegacyPaymentReference.Parse(reference) is { } legacy && legacy.ZevId == zevId)
                result[reference] = legacy;
        return result;
    }

    private async Task<Dictionary<(Guid PropertyUnitId, Guid PersonId), long>> LoadAsync(Guid zevId,
        IReadOnlyCollection<(Guid PropertyUnitId, Guid PersonId)> pairs, CancellationToken cancellationToken)
    {
        var unitIds = pairs.Select(pair => pair.PropertyUnitId).Distinct().ToArray();
        var rows = await database.PaymentAccounts.AsNoTracking()
            .Where(item => item.ZevId == zevId && unitIds.Contains(item.PropertyUnitId))
            .Select(item => new { item.PropertyUnitId, item.PersonId, item.Number }).ToListAsync(cancellationToken);
        var wanted = pairs.ToHashSet();
        return rows.Where(item => wanted.Contains((item.PropertyUnitId, item.PersonId)))
            .ToDictionary(item => (item.PropertyUnitId, item.PersonId), item => item.Number);
    }
}
