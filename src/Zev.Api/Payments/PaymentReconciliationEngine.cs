using System.Data;
using System.Security.Claims;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Payments;

public sealed record StatementImportResult(BankStatementImport? Import, int MatchedCount, string? Error);
public sealed record StatementPreviewResult(IReadOnlyList<ParsedBankTransaction>? Rows, bool IsDuplicate, string? Error);
public sealed record BankReturnResult(bool Success, string? Error);

public sealed class PaymentReconciliationEngine(ZevDbContext database, IEnumerable<IBankStatementParser> parsers,
    IHttpContextAccessor? context = null)
{
    private readonly PaymentReferenceService references = new(database);
    public async Task<StatementPreviewResult> PreviewAsync(Guid zevId, Guid bankAccountId, string fileName,
        string format, byte[] content, CancellationToken cancellationToken)
    {
        var parsed = Parse(fileName, format, content);
        if (parsed.Rows is null) return new(null, false, parsed.Error);
        var account = await database.BankAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == bankAccountId
            && item.ZevId == zevId && item.IsActive && item.Fund.IsActive, cancellationToken);
        if (account is null) return new(null, false, "Bank account not found.");
        if (parsed.Rows.Any(item => item.CurrencyCode is not null
            && !item.CurrencyCode.Equals(account.CurrencyCode, StringComparison.OrdinalIgnoreCase)))
            return new(null, false, "Statement currency does not match account currency.");
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var duplicate = await database.BankStatementImports.AnyAsync(item => item.ZevId == zevId
            && item.BankAccountId == bankAccountId && item.ChecksumSha256 == checksum, cancellationToken);
        return new(parsed.Rows, duplicate, null);
    }

    public async Task<StatementImportResult> ImportAsync(Guid zevId, Guid bankAccountId, string fileName,
        string format, byte[] content, CancellationToken cancellationToken)
    {
        var parsed = Parse(fileName, format, content);
        if (parsed.Rows is null || parsed.Parser is null) return new(null, 0, parsed.Error);
        var rows = parsed.Rows;
        var parser = parsed.Parser;

        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [Zevs] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {zevId}", cancellationToken);
        var account = await database.BankAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == bankAccountId
            && item.ZevId == zevId && item.IsActive && item.Fund.IsActive, cancellationToken);
        if (account is null) return new(null, 0, "Bank account not found.");
        if (rows.Any(item => item.CurrencyCode is not null
            && !item.CurrencyCode.Equals(account.CurrencyCode, StringComparison.OrdinalIgnoreCase)))
            return new(null, 0, "Statement currency does not match account currency.");
        if (await database.BankStatementImports.AnyAsync(item => item.ZevId == zevId
            && item.BankAccountId == bankAccountId && item.ChecksumSha256 == checksum, cancellationToken))
            return new(null, 0, "Statement already imported.");

        var import = new BankStatementImport
        {
            OrganizationId = account.OrganizationId, ZevId = zevId, BankAccountId = account.Id,
            OriginalFileName = Path.GetFileName(fileName), ChecksumSha256 = checksum,
            Format = parser.Format, TransactionCount = rows.Count,
            ImportedByUserId = Guid.TryParse(context?.HttpContext?.User.FindFirst("sub")?.Value,
                out var importerId) ? importerId : null
        };
        database.BankStatementImports.Add(import);
        var transactions = rows.Select((row, index) => new BankTransaction
        {
            OrganizationId = account.OrganizationId, ZevId = zevId, ImportId = import.Id,
            Sequence = index + 1, BookingDate = row.BookingDate, Amount = row.Amount,
            Reference = string.IsNullOrWhiteSpace(row.Reference) ? null : row.Reference.Trim(),
            Counterparty = row.Counterparty?.Trim(), Description = row.Description?.Trim()
        }).ToList();
        database.BankTransactions.AddRange(transactions);
        var payments = transactions.Where(item => item.Amount > 0m).Select(item => new Payment
        {
            OrganizationId = account.OrganizationId, ZevId = zevId, FundId = account.FundId,
            CurrencyCode = account.CurrencyCode, BankTransactionId = item.Id,
            BankTransaction = item, Amount = item.Amount, PaidOn = item.BookingDate
        }).ToList();
        var paymentsByTransaction = payments.ToDictionary(item => item.BankTransactionId!.Value);
        database.Payments.AddRange(payments);
        database.FundTransactions.AddRange(payments.Select(payment => new FundTransaction
        {
            OrganizationId = account.OrganizationId, ZevId = zevId, FundId = account.FundId,
            Kind = FundTransactionKind.Payment, SignedAmount = payment.Amount, SourceId = payment.Id,
            EffectiveOn = payment.PaidOn
        }));

        var references = transactions.Select(item => item.Reference).Where(item => item is not null).Distinct().ToArray();
        var charges = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
            && item.Amount > 0m && references.Contains(item.Reference)).ToListAsync(cancellationToken);
        var accountKeys = await this.references.ResolveAsync(zevId,
            transactions.Select(item => item.Reference), cancellationToken);
        var accountUnitIds = accountKeys.Values.Select(item => item.PropertyUnitId).Distinct().ToArray();
        var accountCharges = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
            && item.Amount > 0m && accountUnitIds.Contains(item.PropertyUnitId))
            .ToListAsync(cancellationToken);
        var chargesByReference = charges.GroupBy(item => item.Reference).ToDictionary(item => item.Key, item => item.ToList());
        var chargeIds = charges.Concat(accountCharges).Select(item => item.Id).Distinct().ToArray();
        var reductions = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
                && item.ReversesChargeId != null && chargeIds.Contains(item.ReversesChargeId.Value))
            .GroupBy(item => item.ReversesChargeId!.Value)
            .Select(group => new { Id = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Id, item => item.Amount, cancellationToken);
        var allocated = await database.PaymentAllocations.AsNoTracking().Where(item => chargeIds.Contains(item.ChargeId)
            && item.Payment.Status == PaymentStatus.Posted)
            .GroupBy(item => item.ChargeId).Select(group => new { chargeId = group.Key, total = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.chargeId, item => item.total, cancellationToken);
        var matched = 0;
        foreach (var bankTransaction in transactions)
        {
            paymentsByTransaction.TryGetValue(bankTransaction.Id, out var payment);
            var candidates = bankTransaction.Reference is not null && chargesByReference.TryGetValue(bankTransaction.Reference, out var found)
                ? found : [];
            var key = bankTransaction.Reference is not null
                && accountKeys.TryGetValue(bankTransaction.Reference, out var resolved) ? resolved : null;
            var eligible = candidates.Count == 1 ? candidates
                : candidates.Count > 1 || key is null || key.ZevId != zevId ? []
                : accountCharges.Where(item => item.PropertyUnitId == key.PropertyUnitId
                    && item.PersonId == key.PersonId).OrderBy(item => item.DueDate).ThenBy(item => item.Id).ToList();
            var remaining = payment?.Amount ?? 0m;
            var allocationCount = 0;
            foreach (var charge in eligible)
            {
                if (payment is null || remaining <= 0m) break;
                if (charge.FundId != account.FundId) continue;
                var open = charge.Amount + reductions.GetValueOrDefault(charge.Id)
                    - allocated.GetValueOrDefault(charge.Id);
                if (open <= 0m) continue;
                var allocation = Math.Min(remaining, open);
                if (payment.PersonId is Guid priorPerson && priorPerson != charge.PersonId) break;
                if (payment.PersonId is null)
                    database.PaymentIdentificationHistories.Add(new PaymentIdentificationHistory
                    {
                        OrganizationId = account.OrganizationId, ZevId = zevId,
                        PaymentId = payment.Id, NewPersonId = charge.PersonId,
                        Reason = "Automatic bank reference reconciliation"
                    });
                payment.PersonId = charge.PersonId;
                database.PaymentAllocations.Add(new PaymentAllocation
                {
                    OrganizationId = account.OrganizationId, ZevId = zevId, PaymentId = payment.Id,
                    ChargeId = charge.Id, Amount = allocation
                });
                allocated[charge.Id] = allocated.GetValueOrDefault(charge.Id) + allocation;
                remaining -= allocation;
                allocationCount++;
            }
            var valid = allocationCount > 0;
            var crossFundOpen = key is not null && key.ZevId == zevId && accountCharges.Any(item =>
                item.PropertyUnitId == key.PropertyUnitId && item.PersonId == key.PersonId
                && item.FundId != account.FundId && item.Amount > allocated.GetValueOrDefault(item.Id));
            bankTransaction.Status = valid && remaining > 0m && crossFundOpen
                ? BankTransactionStatus.PartiallyMatched
                : valid ? BankTransactionStatus.Matched : BankTransactionStatus.ManualReview;
            if (valid) matched++;
            database.ReconciliationAttempts.Add(new ReconciliationAttempt
            {
                OrganizationId = account.OrganizationId, ZevId = zevId, BankTransactionId = bankTransaction.Id,
                PaymentId = payment?.Id, Rule = candidates.Count > 0 ? "EXACT_REFERENCE_V1" : "ACCOUNT_REFERENCE_OLDEST_DUE_V1",
                Status = bankTransaction.Status == BankTransactionStatus.PartiallyMatched
                    ? ReconciliationStatus.PartiallyMatched
                    : valid ? ReconciliationStatus.Matched : candidates.Count > 1
                    ? ReconciliationStatus.Ambiguous : ReconciliationStatus.NoMatch,
                Result = valid ? $"Allocated to {allocationCount} charge(s); unallocated {remaining:0.00}"
                    : "Manual review required"
            });
        }
        import.Status = transactions.All(item => item.Status == BankTransactionStatus.Matched)
            ? BankImportStatus.Reconciled
            : transactions.Any(item => item.Status is BankTransactionStatus.Matched
                or BankTransactionStatus.PartiallyMatched)
                ? BankImportStatus.PartiallyReconciled : BankImportStatus.Imported;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(import, matched, null);
        }
        catch (DbUpdateException)
        {
            return new(null, 0, "Statement conflict.");
        }
    }

    private (IReadOnlyList<ParsedBankTransaction>? Rows, IBankStatementParser? Parser, string? Error)
        Parse(string fileName, string format, byte[] content)
    {
        if (content.Length is 0 or > 5_000_000 || string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
            return (null, null, "Invalid file size or name.");
        var parser = parsers.SingleOrDefault(item => item.Format.Equals(format, StringComparison.OrdinalIgnoreCase));
        if (parser is null) return (null, null, "Unsupported statement format.");
        try
        {
            var decoded = new System.Text.UTF8Encoding(false, true).GetString(content);
            var rows = parser.Parse(decoded);
            if (rows.Count is 0 or > 10000 || rows.Any(item => item.Amount == 0m || item.Reference?.Length > 120
                || item.Counterparty?.Length > 250 || item.Description?.Length > 500))
                return (null, null, "Invalid statement rows.");
            return (rows, parser, null);
        }
        catch (Exception problem) when (problem is FormatException or XmlException or ArgumentException)
        {
            return (null, null, "Statement could not be parsed.");
        }
    }

    public async Task<bool> AllocateManuallyAsync(Guid zevId, Guid bankTransactionId,
        IReadOnlyList<(Guid ChargeId, decimal Amount)> requested, CancellationToken cancellationToken)
    {
        if (requested.Count == 0 || requested.Any(item => item.Amount <= 0m)
            || requested.Select(item => item.ChargeId).Distinct().Count() != requested.Count) return false;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [Zevs] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {zevId}", cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [BankTransactions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {bankTransactionId} AND [ZevId] = {zevId}",
                cancellationToken);
        var payment = await database.Payments.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.BankTransactionId == bankTransactionId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Posted) return false;
        var ids = requested.Select(item => item.ChargeId).ToArray();
        var charges = await database.Charges.Where(item => item.ZevId == zevId && ids.Contains(item.Id)).ToListAsync(cancellationToken);
        if (charges.Count != ids.Length || charges.Any(item => item.Amount <= 0m || item.FundId != payment.FundId)
            || charges.Select(item => item.PersonId).Distinct().Count() != 1
            || (payment.PersonId is Guid personId && charges.Any(item => item.PersonId != personId))) return false;
        var priorPayment = await database.PaymentAllocations.Where(item => item.PaymentId == payment.Id)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        if (priorPayment + requested.Sum(item => item.Amount) > payment.Amount) return false;
        var priorCharges = await database.PaymentAllocations.Where(item => ids.Contains(item.ChargeId)
                && item.Payment.Status == PaymentStatus.Posted)
            .GroupBy(item => item.ChargeId).Select(group => new { chargeId = group.Key, total = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.chargeId, item => item.total, cancellationToken);
        var reductions = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
                && item.ReversesChargeId != null && ids.Contains(item.ReversesChargeId.Value))
            .GroupBy(item => item.ReversesChargeId!.Value)
            .Select(group => new { Id = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Id, item => item.Amount, cancellationToken);
        if (requested.Any(item => item.Amount + priorCharges.GetValueOrDefault(item.ChargeId)
            > charges.Single(charge => charge.Id == item.ChargeId).Amount
                + reductions.GetValueOrDefault(item.ChargeId))) return false;
        if (payment.PersonId is null)
        {
            payment.PersonId = charges[0].PersonId;
            database.PaymentIdentificationHistories.Add(new PaymentIdentificationHistory
            {
                OrganizationId = payment.OrganizationId, ZevId = zevId,
                PaymentId = payment.Id, NewPersonId = charges[0].PersonId,
                Reason = "Manual bank transaction reconciliation",
                ChangedByUserId = Guid.TryParse(context?.HttpContext?.User.FindFirstValue("sub"), out var actorId)
                    ? actorId : null
            });
        }
        database.PaymentAllocations.AddRange(requested.Select(item => new PaymentAllocation
        {
            OrganizationId = payment.OrganizationId, ZevId = zevId, PaymentId = payment.Id,
            ChargeId = item.ChargeId, Amount = item.Amount
        }));
        var bankTransaction = await database.BankTransactions.SingleAsync(item => item.Id == bankTransactionId, cancellationToken);
        bankTransaction.Status = bankTransaction.Status == BankTransactionStatus.PartiallyMatched
            && priorPayment + requested.Sum(item => item.Amount) < payment.Amount
            ? BankTransactionStatus.PartiallyMatched : BankTransactionStatus.Matched;
        database.ReconciliationAttempts.Add(new ReconciliationAttempt
        {
            OrganizationId = payment.OrganizationId, ZevId = zevId, BankTransactionId = bankTransactionId,
            PaymentId = payment.Id, Rule = "MANUAL_V1", Status = ReconciliationStatus.Matched,
            Result = $"Allocated to {requested.Count} charges"
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await RefreshImportStatusAsync(bankTransaction.ImportId, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException) { return false; }
    }

    public async Task<BankReturnResult> ReverseFullPaymentAsync(Guid zevId, Guid returnTransactionId,
        Guid paymentId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 250)
            return new(false, "Reason is required (max 250 characters).");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [Zevs] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {zevId}", cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [Payments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {paymentId} AND [ZevId] = {zevId}",
                cancellationToken);
        var payment = await database.Payments.Include(item => item.BankTransaction)
            .ThenInclude(item => item!.Import).SingleOrDefaultAsync(item => item.Id == paymentId
                && item.ZevId == zevId && item.Status == PaymentStatus.Posted, cancellationToken);
        var returned = await database.BankTransactions.Include(item => item.Import)
            .SingleOrDefaultAsync(item => item.Id == returnTransactionId && item.ZevId == zevId
                && item.Status == BankTransactionStatus.ManualReview && item.Amount < 0m,
                cancellationToken);
        if (payment is null || payment.BankTransaction is null || returned is null || -returned.Amount != payment.Amount
            || payment.BankTransaction.Import.BankAccountId != returned.Import.BankAccountId
            || returned.BookingDate < payment.PaidOn)
            return new(false, "Return must be a full negative transaction on the original bank account.");
        var originalMovement = await database.FundTransactions.SingleOrDefaultAsync(item =>
            item.ZevId == zevId && item.Kind == FundTransactionKind.Payment && item.SourceId == payment.Id,
            cancellationToken);
        if (originalMovement is null) return new(false, "Original fund movement was not found.");
        payment.Status = PaymentStatus.Reversed;
        payment.ReversedOn = returned.BookingDate;
        payment.BankTransaction.Status = BankTransactionStatus.Returned;
        returned.Status = BankTransactionStatus.Returned;
        database.FundTransactions.Add(new FundTransaction
        {
            OrganizationId = payment.OrganizationId, ZevId = zevId, FundId = payment.FundId,
            Kind = FundTransactionKind.Reversal, SignedAmount = returned.Amount,
            SourceId = returned.Id, ReversesTransactionId = originalMovement.Id,
            EffectiveOn = returned.BookingDate
        });
        database.ReconciliationAttempts.Add(new ReconciliationAttempt
        {
            OrganizationId = payment.OrganizationId, ZevId = zevId,
            BankTransactionId = returned.Id, PaymentId = payment.Id,
            Rule = "FULL_BANK_RETURN_V1", Status = ReconciliationStatus.Reversed,
            Result = reason.Trim()
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await RefreshImportStatusAsync(payment.BankTransaction.ImportId, cancellationToken);
            if (returned.ImportId != payment.BankTransaction.ImportId)
                await RefreshImportStatusAsync(returned.ImportId, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, null);
        }
        catch (DbUpdateException) { return new(false, "Return conflict."); }
    }

    private async Task RefreshImportStatusAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await database.BankStatementImports.SingleAsync(item => item.Id == importId,
            cancellationToken);
        var statuses = await database.BankTransactions.AsNoTracking().Where(item => item.ImportId == importId)
            .Select(item => item.Status).ToListAsync(cancellationToken);
        import.Status = statuses.All(item => item is BankTransactionStatus.Matched or BankTransactionStatus.Returned
            or BankTransactionStatus.Ignored) ? BankImportStatus.Reconciled
            : statuses.Any(item => item is BankTransactionStatus.Matched or BankTransactionStatus.PartiallyMatched
                or BankTransactionStatus.Returned) ? BankImportStatus.PartiallyReconciled : BankImportStatus.Imported;
    }
}
