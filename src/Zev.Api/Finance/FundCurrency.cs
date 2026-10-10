using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;

namespace Zev.Api.Finance;

public static class FundCurrency
{
    // The fund's currency: its active bank accounts' single currency, otherwise Finance:DefaultCurrency (BAM).
    // Null when the fund has active accounts in more than one currency (ambiguous, same rule as billing).
    public static async Task<string?> ResolveAsync(ZevDbContext database, Guid zevId, Guid fundId,
        IConfiguration? configuration, CancellationToken cancellationToken)
    {
        var currencies = await database.BankAccounts.AsNoTracking()
            .Where(item => item.ZevId == zevId && item.FundId == fundId && item.IsActive)
            .Select(item => item.CurrencyCode).Distinct().ToListAsync(cancellationToken);
        if (currencies.Count > 1) return null;
        return currencies.SingleOrDefault()?.ToUpperInvariant()
            ?? configuration?["Finance:DefaultCurrency"]?.Trim().ToUpperInvariant() ?? "BAM";
    }
}
