using System.Data;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Finance;

public sealed record StornoRequest(Guid RequestId, string Reason, string? Note);
public sealed record CorrectionLineRequest(Guid? OriginalInvoiceLineId, Guid? FundId,
    string? Description, decimal Amount);
public sealed record CorrectionRequest(Guid RequestId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FinancialDocumentType>))] FinancialDocumentType Type,
    string Reason, string? Note, IReadOnlyList<CorrectionLineRequest> Lines);
public sealed record LifecycleResult(FinancialDocument? Document, string? Error, bool AlreadyExists = false);

public sealed class InvoiceLifecycleService(ZevDbContext database, IInvoiceNumberProvider numbers)
{
    public Task<LifecycleResult> StornoAsync(Guid zevId, Guid invoiceId, StornoRequest request,
        Guid? actorId, CancellationToken cancellationToken) =>
        CreateAsync(zevId, invoiceId, request.RequestId, FinancialDocumentType.Storno,
            request.Reason, request.Note, null, actorId, cancellationToken);

    public Task<LifecycleResult> CorrectAsync(Guid zevId, Guid invoiceId, CorrectionRequest request,
        Guid? actorId, CancellationToken cancellationToken) =>
        CreateAsync(zevId, invoiceId, request.RequestId, request.Type,
            request.Reason, request.Note, request.Lines, actorId, cancellationToken);

    private async Task<LifecycleResult> CreateAsync(Guid zevId, Guid invoiceId, Guid requestId,
        FinancialDocumentType type, string reason, string? note, IReadOnlyList<CorrectionLineRequest>? requestedLines,
        Guid? actorId, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500
            || note?.Length > 1000 || !Enum.IsDefined(type))
            return new(null, "Valid request ID and reason (max 500 characters) are required.");
        if (type != FinancialDocumentType.Storno && (requestedLines is null || requestedLines.Count == 0
            || requestedLines.Count > 100 || requestedLines.Any(line => line.Amount <= 0m
                || decimal.Round(line.Amount, 2) != line.Amount)))
            return new(null, "Correction requires 1-100 positive two-decimal lines.");
        if (type == FinancialDocumentType.Storno && requestedLines is not null)
            return new(null, "Storno lines are derived from the original invoice.");

        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [Zevs] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {zevId}", cancellationToken);
        var existing = await database.FinancialDocuments.AsNoTracking().Include(item => item.Lines).SingleOrDefaultAsync(
            item => item.ZevId == zevId && item.RequestId == requestId, cancellationToken);
        if (existing is not null)
            return existing.OriginalInvoiceId == invoiceId && existing.Type == type
                && existing.Reason == reason.Trim() && existing.Note == note?.Trim()
                && (type == FinancialDocumentType.Storno || SameCorrection(existing, requestedLines!))
                ? new(existing, null, true) : new(null, "Request ID was already used for another document.");

        var invoice = await database.Invoices.Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.ZevId == zevId && item.Id == invoiceId, cancellationToken);
        if (invoice is null) return new(null, "Issued Invoice V2 was not found.");
        if (invoice.DocumentStatus == InvoiceDocumentStatus.Cancelled)
            return new(null, "Invoice has already been cancelled.");
        if (type == FinancialDocumentType.Storno && invoice.DocumentStatus != InvoiceDocumentStatus.Issued)
            return new(null, "Corrected invoices cannot be cancelled; use another correction.");
        if (invoice.DocumentStatus is not (InvoiceDocumentStatus.Issued or InvoiceDocumentStatus.Corrected))
            return new(null, "Only issued invoices can be corrected.");

        var sourceLines = invoice.Lines.ToDictionary(line => line.Id);
        if (sourceLines.Count == 0) return new(null, "Invoice has no lines to reverse.");
        var corrections = await database.FinancialDocuments.AsNoTracking().Include(item => item.Lines)
            .Where(item => item.ZevId == zevId && item.OriginalInvoiceId == invoiceId)
            .ToListAsync(cancellationToken);
        var specifications = new List<(InvoiceLine? Source, Guid FundId, string FundName,
            string Description, decimal SignedAmount)>();
        if (type == FinancialDocumentType.Storno)
        {
            if (corrections.Count != 0) return new(null, "Invoice has related corrections and cannot be cancelled.");
            specifications.AddRange(invoice.Lines.Where(line => line.FinalAmount > 0m)
                .Select(line => ((InvoiceLine?)line, line.FundId, line.FundNameSnapshot,
                    line.DescriptionSnapshot, -line.FinalAmount)));
        }
        else
        {
            foreach (var requested in requestedLines!)
            {
                InvoiceLine? source = null;
                if (requested.OriginalInvoiceLineId is Guid lineId)
                {
                    if (!sourceLines.TryGetValue(lineId, out source))
                        return new(null, "Selected line does not belong to this invoice.");
                    if (requested.FundId is Guid fundId && fundId != source.FundId)
                        return new(null, "Correction fund does not match the selected invoice line.");
                }
                else if (type == FinancialDocumentType.Credit)
                    return new(null, "Credit corrections must reference an original invoice line.");

                var fund = source is null
                    ? await database.Funds.AsNoTracking().SingleOrDefaultAsync(item => item.ZevId == zevId
                        && item.Id == requested.FundId && item.IsActive, cancellationToken) : null;
                if (source is null && (fund is null || !invoice.Lines.Any(line => line.FundId == fund.Id)))
                    return new(null, "New debit line requires an active fund already present on the invoice.");
                var description = string.IsNullOrWhiteSpace(requested.Description)
                    ? source?.DescriptionSnapshot : requested.Description.Trim();
                if (string.IsNullOrWhiteSpace(description) || description.Length > 200)
                    return new(null, "Correction line description must have 1-200 characters.");
                specifications.Add((source, source?.FundId ?? fund!.Id,
                    source?.FundNameSnapshot ?? fund!.Name, description,
                    type == FinancialDocumentType.Credit ? -requested.Amount : requested.Amount));
            }
            if (type == FinancialDocumentType.Credit)
            {
                foreach (var group in specifications.GroupBy(item => item.Source!.Id))
                {
                    var alreadyCredited = corrections.SelectMany(item => item.Lines)
                        .Where(line => line.OriginalInvoiceLineId == group.Key && line.SignedAmount < 0m)
                        .Sum(line => -line.SignedAmount);
                    if (alreadyCredited + group.Sum(item => -item.SignedAmount) > sourceLines[group.Key].FinalAmount)
                        return new(null, "Credit exceeds the original invoice line balance.");
                }
            }
        }
        if (specifications.Count == 0 && type != FinancialDocumentType.Storno)
            return new(null, "Document has no non-zero lines.");

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var series = type == FinancialDocumentType.Storno
            ? FinancialDocumentSeries.Storno : FinancialDocumentSeries.Correction;
        var nextSequence = (await database.FinancialDocuments.AsNoTracking()
            .Where(item => item.ZevId == zevId && item.Series == series && item.NumberYear == today.Year)
            .MaxAsync(item => (int?)item.NumberSequence, cancellationToken) ?? 0) + 1;
        var document = new FinancialDocument
        {
            OrganizationId = invoice.OrganizationId, ZevId = zevId, OriginalInvoiceId = invoice.Id,
            RequestId = requestId, Type = type, Series = series, NumberYear = today.Year,
            NumberSequence = nextSequence, DocumentNumber = numbers.Format(series, today.Year, nextSequence),
            IssueDate = today, Reason = reason.Trim(), Note = note?.Trim(), CreatedByUserId = actorId,
            CreatedUtc = now, SignedAmount = specifications.Sum(item => item.SignedAmount),
            Currency = invoice.Currency, ZevNameSnapshot = invoice.ZevNameSnapshot,
            PersonNameSnapshot = invoice.PersonNameSnapshot, UnitCodeSnapshot = invoice.UnitCodeSnapshot,
            OriginalInvoiceNumberSnapshot = invoice.InvoiceNumber
        };
        foreach (var specification in specifications)
        {
            var charge = new Charge
            {
                OrganizationId = invoice.OrganizationId, ZevId = zevId,
                PropertyUnitId = invoice.PropertyUnitId, PersonId = invoice.PersonId,
                FundId = specification.FundId, FinancialDocumentId = document.Id,
                ReversesChargeId = specification.SignedAmount < 0m ? specification.Source!.ChargeId : null,
                EffectiveOn = today, Period = invoice.Period, DueDate = today,
                BasisQuantity = 1m, RateSnapshot = specification.SignedAmount,
                OwnershipShareSnapshot = 1m, Amount = specification.SignedAmount,
                Note = specification.Description, IsManualOverride = true,
                Reference = $"ZEV-{Guid.NewGuid():N}", CreatedUtc = now
            };
            database.Charges.Add(charge);
            document.Lines.Add(new FinancialDocumentLine
            {
                OrganizationId = invoice.OrganizationId, ZevId = zevId, DocumentId = document.Id,
                OriginalInvoiceLineId = specification.Source?.Id, ChargeId = charge.Id,
                FundId = specification.FundId, FundNameSnapshot = specification.FundName,
                DescriptionSnapshot = specification.Description, SignedAmount = specification.SignedAmount
            });
        }

        var chargeIds = invoice.Lines.Select(line => line.ChargeId)
            .Concat(corrections.SelectMany(item => item.Lines).Select(line => line.ChargeId)).ToArray();
        var allocations = await database.PaymentAllocations.AsNoTracking()
            .Where(item => chargeIds.Contains(item.ChargeId) && item.Payment.Status == PaymentStatus.Posted)
            .Select(item => new { item.Id, item.PaymentId, item.ChargeId, item.Charge.FundId,
                item.Amount, item.ReversesAllocationId, item.CreatedUtc })
            .ToListAsync(cancellationToken);
        var released = allocations.Where(item => item.ReversesAllocationId is not null)
            .GroupBy(item => item.ReversesAllocationId!.Value)
            .ToDictionary(group => group.Key, group => -group.Sum(item => item.Amount));
        foreach (var originalLine in invoice.Lines)
        {
            var capacity = originalLine.FinalAmount
                + corrections.SelectMany(item => item.Lines).Where(line => line.OriginalInvoiceLineId == originalLine.Id
                    && line.SignedAmount < 0m).Sum(line => line.SignedAmount)
                + specifications.Where(item => item.Source?.Id == originalLine.Id
                    && item.SignedAmount < 0m).Sum(item => item.SignedAmount);
            var paid = allocations.Where(item => item.ChargeId == originalLine.ChargeId).Sum(item => item.Amount);
            var excess = paid - capacity;
            if (excess <= 0m) continue;
            foreach (var allocation in allocations.Where(item => item.ChargeId == originalLine.ChargeId
                    && item.Amount > 0m)
                .OrderByDescending(item => item.CreatedUtc))
            {
                var amount = Math.Min(excess, allocation.Amount - released.GetValueOrDefault(allocation.Id));
                if (amount <= 0m) continue;
                database.PaymentAllocations.Add(new PaymentAllocation
                {
                    OrganizationId = invoice.OrganizationId, ZevId = zevId,
                    PaymentId = allocation.PaymentId, ChargeId = allocation.ChargeId,
                    ReversesAllocationId = allocation.Id, FinancialDocumentId = document.Id,
                    Amount = -amount, CreatedUtc = now
                });
                excess -= amount;
                if (excess <= 0m) break;
            }
            if (excess > 0m) return new(null, "Payment allocation state is inconsistent with the invoice.");
        }
        invoice.DocumentStatus = type == FinancialDocumentType.Storno
            ? InvoiceDocumentStatus.Cancelled : InvoiceDocumentStatus.Corrected;
        database.FinancialDocuments.Add(document);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(document, null);
        }
        catch (Exception problem) when (DbUpdateErrors.IsConcurrentWriteConflict(problem))
        {
            return new(null, "Document was concurrently posted or conflicts with an existing document.");
        }
    }

    private static bool SameCorrection(FinancialDocument document,
        IReadOnlyList<CorrectionLineRequest> requestedLines)
    {
        if (document.Lines.Count != requestedLines.Count) return false;
        var remaining = document.Lines.ToList();
        foreach (var requested in requestedLines)
        {
            var match = remaining.FirstOrDefault(line => line.OriginalInvoiceLineId == requested.OriginalInvoiceLineId
                && (requested.FundId is null || line.FundId == requested.FundId)
                && line.SignedAmount == (document.Type == FinancialDocumentType.Credit
                    ? -requested.Amount : requested.Amount)
                && (string.IsNullOrWhiteSpace(requested.Description)
                    || line.DescriptionSnapshot == requested.Description.Trim()));
            if (match is null) return false;
            remaining.Remove(match);
        }
        return true;
    }
}
