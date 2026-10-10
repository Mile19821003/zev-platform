using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Payments;

public sealed record BankStatementBalance(Guid ImportId, string FileName, string Format, DateOnly? StatementFrom,
    DateOnly? StatementTo, DateOnly? FirstBooking, DateOnly? LastBooking, decimal? OpeningBalance,
    decimal? ClosingBalance, decimal Credits, decimal Debits, int TransactionCount, DateTimeOffset ImportedUtc);
public sealed record BankStatementGap(Guid PreviousImportId, Guid NextImportId, decimal PreviousClosing,
    decimal NextOpening, decimal Difference);
public sealed record ReportedBankBalance(decimal Amount, DateOnly? AsOf, Guid ImportId);
public sealed record BankSide(string BalanceSource, ReportedBankBalance? ReportedBalance, decimal? DerivedBalance,
    bool? DerivedMatchesReported, decimal Credits, decimal Debits, decimal Movement, int StatementCount,
    IReadOnlyList<BankStatementBalance> Statements, IReadOnlyList<BankStatementGap> Gaps);
public sealed record FundSide(Guid FundId, string FundName, string CurrencyCode, decimal Balance,
    decimal FromThisAccount, decimal FromOtherAccounts, decimal ManualPayments, decimal Expenses, decimal Other,
    int OtherCurrencyEntries);
public sealed record BalanceReconciliation(decimal BankMovement, decimal FundBalance, decimal Difference,
    decimal BankOutflowsNotInFund, decimal ManualPaymentsNotInBank, decimal ExpensesNotInBank,
    decimal OtherBankAccountsOfFund, decimal OtherFundEntries, decimal Unexplained);
public sealed record BankAccountBalance(Guid AccountId, string AccountNumber, string? BankName, string CurrencyCode,
    bool IsActive, BankSide Bank, FundSide Fund, BalanceReconciliation Reconciliation);

// Bank balance (what the bank statements say) and fund balance (the fund ledger) are kept apart:
// the bank side never reads FundTransactions and the fund side never reads statement balances.
// Reconciliation explains the difference between imported bank movements and the fund balance:
//   movement - fund = outflows not in fund - manual payments - expenses - other accounts - other + unexplained.
public sealed class BankAccountBalanceService(ZevDbContext database)
{
    public async Task<BankAccountBalance?> ReadAsync(Guid zevId, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId && item.Id == accountId)
            .Select(item => new { item.Id, item.AccountNumber, item.BankName, item.CurrencyCode, item.IsActive,
                item.FundId, FundName = item.Fund.Name }).SingleOrDefaultAsync(cancellationToken);
        if (account is null) return null;
        var bank = await BankSideAsync(zevId, account.Id, cancellationToken);
        var fund = await FundSideAsync(zevId, account.Id, account.FundId, account.FundName, account.CurrencyCode,
            cancellationToken);
        var outflowsNotInFund = await database.BankTransactions.AsNoTracking().Where(item => item.ZevId == zevId
                && item.Import.BankAccountId == account.Id && item.Amount < 0m
                && item.Status != BankTransactionStatus.Returned)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        var difference = bank.Movement - fund.Balance;
        var explained = outflowsNotInFund - fund.ManualPayments - fund.Expenses - fund.FromOtherAccounts - fund.Other;
        return new(account.Id, account.AccountNumber, account.BankName, account.CurrencyCode, account.IsActive, bank, fund,
            new(bank.Movement, fund.Balance, difference, outflowsNotInFund, fund.ManualPayments, fund.Expenses,
                fund.FromOtherAccounts, fund.Other, difference - explained));
    }

    private async Task<BankSide> BankSideAsync(Guid zevId, Guid accountId, CancellationToken cancellationToken)
    {
        var imports = await database.BankStatementImports.AsNoTracking().Where(item => item.ZevId == zevId
                && item.BankAccountId == accountId)
            .Select(item => new { item.Id, item.OriginalFileName, item.Format, item.StatementFrom, item.StatementTo,
                item.OpeningBalance, item.ClosingBalance, item.TransactionCount, item.ImportedUtc })
            .ToListAsync(cancellationToken);
        var importIds = imports.Select(item => item.Id).ToArray();
        var totals = (await database.BankTransactions.AsNoTracking().Where(item => item.ZevId == zevId
                && importIds.Contains(item.ImportId))
            .Select(item => new { item.ImportId, item.Amount, item.BookingDate }).ToListAsync(cancellationToken))
            .GroupBy(item => item.ImportId).ToDictionary(group => group.Key, group => new
            {
                Credits = group.Where(item => item.Amount > 0m).Sum(item => item.Amount),
                Debits = group.Where(item => item.Amount < 0m).Sum(item => item.Amount),
                First = group.Min(item => item.BookingDate), Last = group.Max(item => item.BookingDate)
            });
        var statements = imports.Select(item =>
        {
            var total = totals.GetValueOrDefault(item.Id);
            return new BankStatementBalance(item.Id, item.OriginalFileName, item.Format, item.StatementFrom,
                item.StatementTo, total?.First, total?.Last, item.OpeningBalance, item.ClosingBalance,
                total?.Credits ?? 0m, total?.Debits ?? 0m, item.TransactionCount, item.ImportedUtc);
        })
            // Chronological: by the stated period end (or last booking date), then by import time.
            .OrderBy(item => item.StatementTo ?? item.LastBooking ?? DateOnly.MaxValue).ThenBy(item => item.ImportedUtc)
            .ToList();
        var gaps = statements.Zip(statements.Skip(1))
            .Where(pair => pair.First.ClosingBalance is not null && pair.Second.OpeningBalance is not null
                && pair.First.ClosingBalance != pair.Second.OpeningBalance)
            .Select(pair => new BankStatementGap(pair.First.ImportId, pair.Second.ImportId,
                pair.First.ClosingBalance!.Value, pair.Second.OpeningBalance!.Value,
                pair.Second.OpeningBalance!.Value - pair.First.ClosingBalance!.Value)).ToList();
        var latest = statements.LastOrDefault(item => item.ClosingBalance is not null);
        var reported = latest is null ? null
            : new ReportedBankBalance(latest.ClosingBalance!.Value, latest.StatementTo ?? latest.LastBooking, latest.ImportId);
        var credits = statements.Sum(item => item.Credits);
        var debits = statements.Sum(item => item.Debits);
        // Only when the oldest statement states its opening balance can the current balance be computed.
        decimal? derived = statements.FirstOrDefault()?.OpeningBalance is decimal opening ? opening + credits + debits : null;
        return new(reported is null ? "NotProvided" : "Statement", reported, derived,
            reported is null || derived is null ? null : reported.Amount == derived,
            credits, debits, credits + debits, statements.Count, statements, gaps);
    }

    private async Task<FundSide> FundSideAsync(Guid zevId, Guid accountId, Guid fundId, string fundName,
        string currencyCode, CancellationToken cancellationToken)
    {
        var ledger = await database.FundTransactions.AsNoTracking().Where(item => item.ZevId == zevId && item.FundId == fundId)
            .Select(item => new { item.Kind, item.SignedAmount, item.SourceId,
                ReversedKind = item.ReversesTransaction == null ? (FundTransactionKind?)null : item.ReversesTransaction.Kind,
                ReversedSourceId = item.ReversesTransaction == null ? null : item.ReversesTransaction.SourceId })
            .ToListAsync(cancellationToken);
        var payments = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId && item.FundId == fundId)
            .Select(item => new { item.Id, item.SourceType, item.CurrencyCode,
                AccountId = item.BankTransaction == null ? (Guid?)null : item.BankTransaction.Import.BankAccountId })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var expenses = await database.Expenses.AsNoTracking().Where(item => item.ZevId == zevId && item.FundId == fundId)
            .Select(item => new { item.Id, item.CurrencyCode }).ToDictionaryAsync(item => item.Id, cancellationToken);
        decimal fromThisAccount = 0m, fromOtherAccounts = 0m, manual = 0m, expenseTotal = 0m, other = 0m;
        var otherCurrency = 0;
        foreach (var entry in ledger)
        {
            var (kind, sourceId) = entry.Kind == FundTransactionKind.Reversal
                ? (entry.ReversedKind, entry.ReversedSourceId) : (entry.Kind, entry.SourceId);
            if (kind == FundTransactionKind.Payment && sourceId is Guid paymentId
                && payments.TryGetValue(paymentId, out var payment))
            {
                if (!payment.CurrencyCode.Equals(currencyCode, StringComparison.OrdinalIgnoreCase)) { otherCurrency++; continue; }
                if (payment.SourceType == PaymentSourceType.Manual) manual += entry.SignedAmount;
                else if (payment.AccountId == accountId) fromThisAccount += entry.SignedAmount;
                else fromOtherAccounts += entry.SignedAmount;
            }
            else if (kind == FundTransactionKind.Expense && sourceId is Guid expenseId
                && expenses.TryGetValue(expenseId, out var expense))
            {
                if (!expense.CurrencyCode.Equals(currencyCode, StringComparison.OrdinalIgnoreCase)) { otherCurrency++; continue; }
                expenseTotal += entry.SignedAmount;
            }
            else other += entry.SignedAmount;
        }
        return new(fundId, fundName, currencyCode.ToUpperInvariant(),
            fromThisAccount + fromOtherAccounts + manual + expenseTotal + other,
            fromThisAccount, fromOtherAccounts, manual, expenseTotal, other, otherCurrency);
    }
}
