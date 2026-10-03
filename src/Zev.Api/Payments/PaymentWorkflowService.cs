using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Payments;

public sealed record PaymentAllocationInput(Guid ChargeId, decimal Amount);
public sealed record ManualPaymentInput(Guid RequestId, Guid PersonId, Guid? PropertyUnitId, Guid FundId,
    DateOnly PaidOn, decimal Amount, string CurrencyCode, string Method, string Reason,
    string? Reference, string? Description, IReadOnlyList<PaymentAllocationInput>? Allocations);
public sealed record PaymentWorkflowResult(Guid? PaymentId, string? Error, bool AlreadyExists = false)
{
    public bool Success => PaymentId is not null;
}
public sealed record AutoAllocationLine(Guid ChargeId, Guid PropertyUnitId, DateOnly Period,
    DateOnly DueDate, string? InvoiceNumber, string Reference, decimal Amount);
public sealed record AutoAllocationPreview(decimal Available, decimal Allocated,
    IReadOnlyList<AutoAllocationLine> Lines, string? Error);

public sealed class PaymentWorkflowService(ZevDbContext database, IConfiguration? configuration = null)
{
    private static bool ValidMoney(decimal value) => value > 0m && decimal.Round(value, 2) == value;
    private string DefaultCurrency => configuration?["Finance:DefaultCurrency"]?.Trim().ToUpperInvariant() ?? "BAM";

    public async Task<PaymentWorkflowResult> CreateManualAsync(Guid zevId, ManualPaymentInput request,
        Guid? actorId, CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty || request.PersonId == Guid.Empty || !ValidMoney(request.Amount)
            || request.PaidOn == default || request.PaidOn > DateOnly.FromDateTime(DateTime.UtcNow)
            || request.CurrencyCode?.Length != 3 || !request.CurrencyCode.All(char.IsLetter)
            || string.IsNullOrWhiteSpace(request.Method) || request.Method.Length > 50
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500
            || request.Reference?.Length > 120 || request.Description?.Length > 500)
            return new(null, "Invalid manual payment fields.");
        if (request.Method.Equals("Cash", StringComparison.OrdinalIgnoreCase)
            && configuration?.GetValue<bool>("Payments:AllowManualCash") != true)
            return new(null, "Cash payments are disabled by Payments:AllowManualCash.");
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await LockZevAsync(zevId, cancellationToken);
        var existing = await database.Payments.AsNoTracking().SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.RequestId == request.RequestId, cancellationToken);
        if (existing is not null)
        {
            var originalAllocations = await database.PaymentAllocations.AsNoTracking().Where(item =>
                    item.PaymentId == existing.Id && item.OperationId == request.RequestId && item.Amount > 0m)
                .Select(item => new PaymentAllocationInput(item.ChargeId, item.Amount))
                .ToListAsync(cancellationToken);
            var requestedAllocations = request.Allocations ?? [];
            return existing.SourceType == PaymentSourceType.Manual && existing.PersonId == request.PersonId
                && existing.FundId == request.FundId && existing.Amount == request.Amount
                && existing.PaidOn == request.PaidOn && existing.ManualMethod == request.Method.Trim()
                && existing.ManualReason == request.Reason.Trim()
                && existing.CurrencyCode == request.CurrencyCode.ToUpperInvariant()
                && existing.ManualUnitId == request.PropertyUnitId
                && existing.ManualReference == request.Reference?.Trim()
                && existing.ManualDescription == request.Description?.Trim()
                && originalAllocations.Count == requestedAllocations.Count
                && originalAllocations.All(item => requestedAllocations.Any(requested => requested == item))
                ? new(existing.Id, null, true) : new(null, "Request ID was used for another payment.");
        }
        var fund = await database.Funds.AsNoTracking().SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Id == request.FundId && item.IsActive, cancellationToken);
        if (fund is null || !await database.People.AsNoTracking().AnyAsync(item => item.OrganizationId == fund.OrganizationId
            && item.Id == request.PersonId, cancellationToken)) return new(null, "Fund or person was not found in this organization.");
        if (!await database.Ownerships.AsNoTracking().AnyAsync(item => item.ZevId == zevId
                && item.PersonId == request.PersonId, cancellationToken)
            && !await database.Charges.AsNoTracking().AnyAsync(item => item.ZevId == zevId
                && item.PersonId == request.PersonId, cancellationToken))
            return new(null, "Person has no ownership or debt history in this ZEV.");
        var accountCurrencies = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId
            && item.FundId == fund.Id && item.IsActive).Select(item => item.CurrencyCode)
            .Distinct().ToListAsync(cancellationToken);
        if (accountCurrencies.Count > 0 && !accountCurrencies.Contains(request.CurrencyCode.ToUpperInvariant()))
            return new(null, "Payment currency does not match an active fund bank account.");
        if (accountCurrencies.Count == 0 && request.CurrencyCode.ToUpperInvariant() != DefaultCurrency)
            return new(null, "Payment currency does not match the configured default currency.");
        if (request.PropertyUnitId is Guid unitId && !await database.Ownerships.AsNoTracking().AnyAsync(item =>
            item.ZevId == zevId && item.PropertyUnitId == unitId && item.PersonId == request.PersonId
            && item.ValidFrom <= request.PaidOn && (item.ValidTo == null || item.ValidTo > request.PaidOn), cancellationToken))
            return new(null, "Selected unit does not belong to the person on payment date.");
        var payment = new Payment
        {
            OrganizationId = fund.OrganizationId, ZevId = zevId, FundId = fund.Id,
            PersonId = request.PersonId, SourceType = PaymentSourceType.Manual, RequestId = request.RequestId,
            Amount = request.Amount, CurrencyCode = request.CurrencyCode.ToUpperInvariant(), PaidOn = request.PaidOn,
            ManualMethod = request.Method.Trim(), ManualReason = request.Reason.Trim(),
            ManualReference = request.Reference?.Trim(), ManualDescription = request.Description?.Trim(),
            ManualUnitId = request.PropertyUnitId,
            CreatedByUserId = actorId
        };
        var lines = request.Allocations ?? [];
        if (lines.Count > 0)
        {
            var error = await ValidateAndStageAsync(payment, lines, request.RequestId, cancellationToken);
            if (error is not null) return new(null, error);
        }
        database.Payments.Add(payment);
        database.FundTransactions.Add(new FundTransaction
        {
            OrganizationId = payment.OrganizationId, ZevId = zevId, FundId = payment.FundId,
            Kind = FundTransactionKind.Payment, SignedAmount = payment.Amount, SourceId = payment.Id,
            EffectiveOn = payment.PaidOn
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(payment.Id, null);
        }
        catch (DbUpdateException) { return new(null, "Manual payment conflict; refresh and retry."); }
    }

    public async Task<PaymentWorkflowResult> AllocateAsync(Guid zevId, Guid paymentId, Guid operationId,
        IReadOnlyList<PaymentAllocationInput> lines, CancellationToken cancellationToken, Guid? actorId = null)
    {
        if (operationId == Guid.Empty) return new(null, "Request ID is required.");
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await LockZevAsync(zevId, cancellationToken);
        var payment = await database.Payments.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Id == paymentId && item.Status == PaymentStatus.Posted, cancellationToken);
        if (payment is null) return new(null, "Posted payment was not found.");
        var existing = await database.PaymentAllocations.AsNoTracking().Where(item => item.ZevId == zevId
            && item.PaymentId == paymentId && item.OperationId == operationId)
            .Select(item => new { item.ChargeId, item.Amount }).ToListAsync(cancellationToken);
        if (existing.Count > 0)
            return existing.Count == lines.Count && existing.All(item => lines.Any(line =>
                line.ChargeId == item.ChargeId && line.Amount == item.Amount))
                ? new(paymentId, null, true) : new(null, "Request ID was used for another allocation.");
        var identifiedPersonId = (Guid?)null;
        if (payment.PersonId is null)
        {
            var chargeIds = lines.Select(item => item.ChargeId).Distinct().ToArray();
            var owners = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
                    && chargeIds.Contains(item.Id) && item.FundId == payment.FundId)
                .Select(item => item.PersonId).Distinct().ToListAsync(cancellationToken);
            if (owners.Count != 1 || chargeIds.Length != lines.Count)
                return new(null, "All selected charges must belong to one identified payer and fund.");
            payment.PersonId = owners[0];
            identifiedPersonId = owners[0];
        }
        var error = await ValidateAndStageAsync(payment, lines, operationId, cancellationToken);
        if (error is not null)
        {
            if (identifiedPersonId is not null) payment.PersonId = null;
            return new(null, error);
        }
        if (identifiedPersonId is Guid identified)
            AddIdentification(payment, null, identified, "Identification by manual allocation", actorId);
        if (payment.BankTransactionId is Guid transactionId)
        {
            var bank = await database.BankTransactions.SingleAsync(item => item.Id == transactionId
                && item.ZevId == zevId, cancellationToken);
            bank.Status = BankTransactionStatus.Matched;
            database.ReconciliationAttempts.Add(new ReconciliationAttempt
            {
                OrganizationId = payment.OrganizationId, ZevId = zevId,
                BankTransactionId = transactionId, PaymentId = paymentId,
                Rule = "MANUAL_V2", Status = ReconciliationStatus.Matched,
                Result = $"Allocated to {lines.Count} charge(s); request {operationId:N}"
            });
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            if (payment.BankTransactionId is Guid bankId)
                await RefreshImportStatusAsync(bankId, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(paymentId, null);
        }
        catch (DbUpdateException) { return new(null, "Allocation conflict; refresh and retry."); }
    }

    public async Task<AutoAllocationPreview> PreviewAutomaticAsync(Guid zevId, Guid paymentId,
        Guid? propertyUnitId, CancellationToken cancellationToken, int maxCharges = 1000)
    {
        var payment = await database.Payments.AsNoTracking().SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Id == paymentId && item.Status == PaymentStatus.Posted, cancellationToken);
        if (payment is null || payment.PersonId is null)
            return new(0m, 0m, [], "Identify the payer before automatic allocation.");
        var used = await database.PaymentAllocations.AsNoTracking().Where(item => item.PaymentId == paymentId)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        var available = payment.Amount - used;
        if (available <= 0m) return new(0m, 0m, [], null);
        var defaultCurrency = DefaultCurrency;
        var charges = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
            && item.PersonId == payment.PersonId && item.FundId == payment.FundId && item.Amount > 0m
            && (propertyUnitId == null || item.PropertyUnitId == propertyUnitId)
            && (database.InvoiceLines.Any(line => line.ChargeId == item.Id
                    && line.Invoice.Currency == payment.CurrencyCode)
                || !database.InvoiceLines.Any(line => line.ChargeId == item.Id)
                    && (database.BankAccounts.Any(account => account.ZevId == zevId
                        && account.FundId == item.FundId && account.IsActive
                        && account.CurrencyCode == payment.CurrencyCode)
                        || !database.BankAccounts.Any(account => account.ZevId == zevId
                            && account.FundId == item.FundId && account.IsActive)
                            && payment.CurrencyCode == defaultCurrency)))
            .Select(item => new { item.Id, item.PropertyUnitId, item.Period, item.DueDate, item.Reference,
                item.Amount, invoiceNumber = database.InvoiceLines.Where(line => line.ChargeId == item.Id)
                    .Select(line => line.Invoice.InvoiceNumber).FirstOrDefault(),
                reduced = database.Charges.Where(reversal => reversal.ReversesChargeId == item.Id)
                    .Sum(reversal => (decimal?)reversal.Amount) ?? 0m,
                paid = database.PaymentAllocations.Where(allocation => allocation.ChargeId == item.Id
                    && allocation.Payment.Status == PaymentStatus.Posted)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m })
            .Where(item => item.Amount + item.reduced - item.paid > 0m)
            .OrderBy(item => item.DueDate).ThenBy(item => item.Period).ThenBy(item => item.Id)
            .Take(maxCharges + 1).ToListAsync(cancellationToken);
        if (charges.Count > maxCharges)
            return new(available, 0m, [], $"Automatic allocation exceeds the {maxCharges}-charge limit.");
        var lines = new List<AutoAllocationLine>();
        var remaining = available;
        foreach (var charge in charges)
        {
            var open = charge.Amount + charge.reduced - charge.paid;
            if (open <= 0m) continue;
            var amount = Math.Min(remaining, open);
            lines.Add(new(charge.Id, charge.PropertyUnitId, charge.Period, charge.DueDate,
                charge.invoiceNumber, charge.Reference, amount));
            remaining -= amount;
            if (remaining <= 0m) break;
        }
        return new(available, available - remaining, lines, null);
    }

    public async Task<string?> ApplyAvailableCreditsToChargesAsync(Guid zevId, Guid billingRunId,
        IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var payments = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId
                && item.Status == PaymentStatus.Posted && item.PersonId != null
                && personIds.Contains(item.PersonId.Value))
            .OrderBy(item => item.PaidOn).ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.Amount,
                allocated = database.PaymentAllocations.Where(allocation => allocation.PaymentId == item.Id)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m })
            .Where(item => item.Amount > item.allocated).ToListAsync(cancellationToken);
        foreach (var item in payments)
        {
            var preview = await PreviewAutomaticAsync(zevId, item.Id, null, cancellationToken, 10000);
            if (preview.Error is not null) return preview.Error;
            if (preview.Lines.Count == 0) continue;
            var payment = await database.Payments.SingleAsync(candidate => candidate.Id == item.Id,
                cancellationToken);
            var batchNumber = 0;
            foreach (var batch in preview.Lines.Chunk(100))
            {
                var hash = SHA256.HashData(billingRunId.ToByteArray()
                    .Concat(item.Id.ToByteArray()).Concat(BitConverter.GetBytes(batchNumber++))
                    .ToArray());
                var operationId = new Guid(hash.AsSpan(0, 16));
                var error = await ValidateAndStageAsync(payment, batch.Select(line =>
                    new PaymentAllocationInput(line.ChargeId, line.Amount)).ToList(), operationId,
                    cancellationToken);
                if (error is not null) return error;
                await database.SaveChangesAsync(cancellationToken);
            }
            if (payment.BankTransactionId is Guid bankId)
            {
                var bank = await database.BankTransactions.SingleAsync(candidate => candidate.Id == bankId,
                    cancellationToken);
                bank.Status = BankTransactionStatus.Matched;
            }
            await database.SaveChangesAsync(cancellationToken);
            if (payment.BankTransactionId is Guid transactionId)
                await RefreshImportStatusAsync(transactionId, cancellationToken);
        }
        return null;
    }

    public async Task<PaymentWorkflowResult> IdentifyAsync(Guid zevId, Guid paymentId, Guid personId,
        CancellationToken cancellationToken, Guid? actorId = null)
    {
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await LockZevAsync(zevId, cancellationToken);
        var payment = await database.Payments.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Id == paymentId && item.Status == PaymentStatus.Posted, cancellationToken);
        if (payment is null) return new(null, "Payment was not found.");
        if (payment.PersonId is not null)
            return payment.PersonId == personId ? new(paymentId, null, true)
                : new(null, "Payer is already identified; reverse incorrect allocations before correction.");
        if (!await database.People.AsNoTracking().AnyAsync(item => item.OrganizationId == payment.OrganizationId
                && item.Id == personId, cancellationToken)
            || !await database.Ownerships.AsNoTracking().AnyAsync(item => item.ZevId == zevId
                && item.PersonId == personId, cancellationToken)
                && !await database.Charges.AsNoTracking().AnyAsync(item => item.ZevId == zevId
                    && item.PersonId == personId, cancellationToken))
            return new(null, "Person has no ownership or debt history in this ZEV.");
        payment.PersonId = personId;
        AddIdentification(payment, null, personId, "Manual payer identification", actorId);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(paymentId, null);
    }

    public async Task<PaymentWorkflowResult> ChangePayerAsync(Guid zevId, Guid paymentId,
        Guid newPersonId, string reason, IReadOnlyList<PaymentAllocationInput>? allocations,
        Guid? actorId, CancellationToken cancellationToken)
    {
        if (newPersonId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return new(null, "New payer and reason (max 500 characters) are required.");
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await LockZevAsync(zevId, cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [Payments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {paymentId} AND [ZevId] = {zevId}",
                cancellationToken);
        var payment = await database.Payments.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Id == paymentId && item.Status == PaymentStatus.Posted, cancellationToken);
        if (payment is null || payment.PersonId is null) return new(null, "Identified posted payment was not found.");
        if (payment.PersonId == newPersonId) return new(null, "New payer must differ from current payer.");
        if (!await database.People.AsNoTracking().AnyAsync(item => item.OrganizationId == payment.OrganizationId
                && item.Id == newPersonId, cancellationToken)
            || !await database.Ownerships.AsNoTracking().AnyAsync(item => item.ZevId == zevId
                && item.PersonId == newPersonId, cancellationToken)
                && !await database.Charges.AsNoTracking().AnyAsync(item => item.ZevId == zevId
                    && item.PersonId == newPersonId, cancellationToken))
            return new(null, "New payer has no ownership or debt history in this ZEV.");
        var active = await database.PaymentAllocations.AsNoTracking().Where(item => item.PaymentId == paymentId
                && item.Amount > 0m && !database.PaymentAllocations.Any(reversal =>
                    reversal.ReversesAllocationId == item.Id))
            .ToListAsync(cancellationToken);
        var released = active.Sum(item => item.Amount);
        var previousPersonId = payment.PersonId;
        payment.PersonId = newPersonId;
        if (allocations is { Count: > 0 })
        {
            var error = await ValidateAndStageAsync(payment, allocations, Guid.NewGuid(), cancellationToken, released);
            if (error is not null)
            {
                payment.PersonId = previousPersonId;
                return new(null, error);
            }
        }
        foreach (var original in active)
            database.PaymentAllocations.Add(new PaymentAllocation
            {
                OrganizationId = payment.OrganizationId, ZevId = zevId, PaymentId = paymentId,
                ChargeId = original.ChargeId, Amount = -original.Amount,
                ReversesAllocationId = original.Id, ReversalReason = reason.Trim()
            });
        AddIdentification(payment, previousPersonId, newPersonId, reason.Trim(), actorId);
        if (payment.BankTransactionId is Guid bankId)
        {
            var bank = await database.BankTransactions.SingleAsync(item => item.Id == bankId
                && item.ZevId == zevId, cancellationToken);
            var newAllocated = (await database.PaymentAllocations.AsNoTracking()
                .Where(item => item.PaymentId == paymentId).SumAsync(item => (decimal?)item.Amount,
                    cancellationToken) ?? 0m) - released + (allocations?.Sum(item => item.Amount) ?? 0m);
            bank.Status = newAllocated == 0m ? BankTransactionStatus.ManualReview
                : newAllocated == payment.Amount ? BankTransactionStatus.Matched
                : BankTransactionStatus.PartiallyMatched;
            database.ReconciliationAttempts.Add(new ReconciliationAttempt
            {
                OrganizationId = payment.OrganizationId, ZevId = zevId,
                BankTransactionId = bankId, PaymentId = paymentId,
                Rule = "PAYER_CORRECTION_V2", Status = ReconciliationStatus.Reversed,
                Result = reason.Trim().Length > 300 ? reason.Trim()[..300] : reason.Trim()
            });
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            if (payment.BankTransactionId is Guid transactionId)
                await RefreshImportStatusAsync(transactionId, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(paymentId, null);
        }
        catch (DbUpdateException) { return new(null, "Payer correction conflict; refresh and retry."); }
    }

    private void AddIdentification(Payment payment, Guid? previousPersonId, Guid newPersonId,
        string reason, Guid? actorId) => database.PaymentIdentificationHistories.Add(new PaymentIdentificationHistory
    {
        OrganizationId = payment.OrganizationId, ZevId = payment.ZevId, PaymentId = payment.Id,
        PreviousPersonId = previousPersonId, NewPersonId = newPersonId,
        Reason = reason, ChangedByUserId = actorId
    });

    public async Task<PaymentWorkflowResult> ReverseAllocationAsync(Guid zevId, Guid allocationId,
        string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            return new(null, "Reason is required (max 500 characters).");
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await LockZevAsync(zevId, cancellationToken);
        var original = await database.PaymentAllocations.Include(item => item.Payment)
            .SingleOrDefaultAsync(item => item.ZevId == zevId && item.Id == allocationId
                && item.Amount > 0m && item.Payment.Status == PaymentStatus.Posted, cancellationToken);
        if (original is null) return new(null, "Active allocation was not found.");
        var existing = await database.PaymentAllocations.AsNoTracking().Where(item =>
            item.ReversesAllocationId == allocationId).Select(item => new { item.ReversalReason,
                item.FinancialDocumentId }).SingleOrDefaultAsync(cancellationToken);
        if (existing is not null) return existing.FinancialDocumentId is null
            && existing.ReversalReason == reason.Trim() ? new(original.PaymentId, null, true)
            : new(null, "Allocation was already reversed by a different operation.");
        database.PaymentAllocations.Add(new PaymentAllocation
        {
            OrganizationId = original.OrganizationId, ZevId = zevId, PaymentId = original.PaymentId,
            ChargeId = original.ChargeId, Amount = -original.Amount,
            ReversesAllocationId = original.Id, ReversalReason = reason.Trim()
        });
        if (original.Payment.BankTransactionId is Guid transactionId)
        {
            var bank = await database.BankTransactions.SingleAsync(item => item.ZevId == zevId
                && item.Id == transactionId, cancellationToken);
            var priorAllocated = await database.PaymentAllocations.AsNoTracking().Where(item =>
                item.PaymentId == original.PaymentId).SumAsync(item => (decimal?)item.Amount,
                cancellationToken) ?? 0m;
            bank.Status = priorAllocated - original.Amount > 0m
                ? BankTransactionStatus.PartiallyMatched : BankTransactionStatus.ManualReview;
            database.ReconciliationAttempts.Add(new ReconciliationAttempt
            {
                OrganizationId = original.OrganizationId, ZevId = zevId,
                BankTransactionId = transactionId, PaymentId = original.PaymentId,
                Rule = "ALLOCATION_REVERSAL_V2", Status = ReconciliationStatus.Rejected,
                Result = reason.Trim().Length > 250 ? reason.Trim()[..250] : reason.Trim()
            });
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            if (original.Payment.BankTransactionId is Guid bankId) await RefreshImportStatusAsync(bankId, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(original.PaymentId, null);
        }
        catch (DbUpdateException) { return new(null, "Allocation reversal conflict; refresh and retry."); }
    }

    private async Task<string?> ValidateAndStageAsync(Payment payment,
        IReadOnlyList<PaymentAllocationInput> lines, Guid operationId, CancellationToken cancellationToken,
        decimal releasedAmount = 0m)
    {
        if (lines.Count is < 1 or > 1000 || lines.Any(line => !ValidMoney(line.Amount))
            || lines.Select(line => line.ChargeId).Distinct().Count() != lines.Count)
            return "Choose 1–1000 distinct charges with positive two-decimal amounts.";
        var ids = lines.Select(line => line.ChargeId).ToArray();
        var charges = await database.Charges.AsNoTracking().Where(item => item.ZevId == payment.ZevId
            && ids.Contains(item.Id)).ToListAsync(cancellationToken);
        if (charges.Count != ids.Length || charges.Any(item => item.Amount <= 0m
            || item.FundId != payment.FundId || item.PersonId != payment.PersonId
            || item.OrganizationId != payment.OrganizationId))
            return "Charge must belong to the same owner, fund and ZEV as the payment.";
        var invoiceCurrencies = await database.InvoiceLines.AsNoTracking().Where(item => ids.Contains(item.ChargeId))
            .Select(item => item.Invoice.Currency).Distinct().ToListAsync(cancellationToken);
        if (invoiceCurrencies.Any(currency => currency != payment.CurrencyCode))
            return "Payment and invoice currencies do not match.";
        if (invoiceCurrencies.Count == 0 && !await database.BankAccounts.AsNoTracking().AnyAsync(item =>
                item.ZevId == payment.ZevId && item.FundId == payment.FundId && item.IsActive
                && item.CurrencyCode == payment.CurrencyCode, cancellationToken)
            && payment.CurrencyCode != DefaultCurrency)
            return "Legacy charge currency cannot be verified for this payment.";
        var priorPayment = await database.PaymentAllocations.AsNoTracking().Where(item => item.PaymentId == payment.Id)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        if (priorPayment - releasedAmount + lines.Sum(item => item.Amount) > payment.Amount)
            return "Allocation exceeds available payment credit.";
        var priorCharges = await database.PaymentAllocations.AsNoTracking().Where(item => ids.Contains(item.ChargeId)
                && item.Payment.Status == PaymentStatus.Posted)
            .GroupBy(item => item.ChargeId).Select(group => new { Id = group.Key, Total = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Id, item => item.Total, cancellationToken);
        var reductions = await database.Charges.AsNoTracking().Where(item => item.ZevId == payment.ZevId
                && item.ReversesChargeId != null && ids.Contains(item.ReversesChargeId.Value))
            .GroupBy(item => item.ReversesChargeId!.Value)
            .Select(group => new { Id = group.Key, Total = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Id, item => item.Total, cancellationToken);
        if (lines.Any(line => line.Amount + priorCharges.GetValueOrDefault(line.ChargeId)
            > charges.Single(charge => charge.Id == line.ChargeId).Amount + reductions.GetValueOrDefault(line.ChargeId)))
            return "Allocation exceeds the open charge amount.";
        database.PaymentAllocations.AddRange(lines.Select(line => new PaymentAllocation
        {
            OrganizationId = payment.OrganizationId, ZevId = payment.ZevId, PaymentId = payment.Id,
            ChargeId = line.ChargeId, Amount = line.Amount, OperationId = operationId
        }));
        return null;
    }

    private async Task LockZevAsync(Guid zevId, CancellationToken cancellationToken)
    {
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [Zevs] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {zevId}", cancellationToken);
    }

    private async Task RefreshImportStatusAsync(Guid bankTransactionId, CancellationToken cancellationToken)
    {
        var importId = await database.BankTransactions.AsNoTracking().Where(item => item.Id == bankTransactionId)
            .Select(item => item.ImportId).SingleAsync(cancellationToken);
        var import = await database.BankStatementImports.SingleAsync(item => item.Id == importId, cancellationToken);
        var statuses = await database.BankTransactions.AsNoTracking().Where(item => item.ImportId == importId)
            .Select(item => item.Status).ToListAsync(cancellationToken);
        import.Status = statuses.All(item => item is BankTransactionStatus.Matched or BankTransactionStatus.Ignored
            or BankTransactionStatus.Returned) ? BankImportStatus.Reconciled
            : statuses.Any(item => item is BankTransactionStatus.Matched or BankTransactionStatus.PartiallyMatched
                or BankTransactionStatus.Returned) ? BankImportStatus.PartiallyReconciled : BankImportStatus.Imported;
        await database.SaveChangesAsync(cancellationToken);
    }
}
