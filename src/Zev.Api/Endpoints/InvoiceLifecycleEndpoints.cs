using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Reporting;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class InvoiceLifecycleEndpoints
{
    public static RouteGroupBuilder MapInvoiceLifecycleEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/finance/invoices/{invoiceId:guid}");
        group.MapPost("/storno", async (Guid zevId, Guid invoiceId, StornoRequest request,
            TenantAccess access, InvoiceLifecycleService lifecycle, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Billing.Reverse", cancellationToken) is { } denied) return denied;
            var result = await lifecycle.StornoAsync(zevId, invoiceId, request, access.CurrentUserId, cancellationToken);
            return result.Document is null ? Results.Conflict(new { error = result.Error })
                : Results.Ok(new { result.Document.Id, result.Document.DocumentNumber,
                    result.Document.SignedAmount, result.AlreadyExists });
        });
        group.MapPost("/corrections", async (Guid zevId, Guid invoiceId, CorrectionRequest request,
            TenantAccess access, InvoiceLifecycleService lifecycle, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Invoice.Correct", cancellationToken) is { } denied) return denied;
            var result = await lifecycle.CorrectAsync(zevId, invoiceId, request, access.CurrentUserId, cancellationToken);
            return result.Document is null ? Results.Conflict(new { error = result.Error })
                : Results.Ok(new { result.Document.Id, result.Document.DocumentNumber,
                    result.Document.SignedAmount, result.AlreadyExists });
        });
        group.MapGet("/documents", async (Guid zevId, Guid invoiceId, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var invoice = await ReadAllowedInvoiceAsync(zevId, invoiceId, access, database, cancellationToken);
            if (invoice is null) return Results.NotFound();
            var documents = await database.FinancialDocuments.AsNoTracking()
                .Where(item => item.ZevId == zevId && item.OriginalInvoiceId == invoiceId)
                .OrderBy(item => item.CreatedUtc)
                .Select(item => new { item.Id, item.DocumentNumber, type = item.Type.ToString(),
                    status = item.DocumentStatus.ToString(),
                    item.IssueDate, item.Reason, item.Note, item.SignedAmount,
                    item.CreatedByUserId, item.CreatedUtc })
                .ToListAsync(cancellationToken);
            var actorNames = await ActorNamesAsync(database, invoice.OrganizationId,
                documents.Select(item => item.CreatedByUserId), cancellationToken);
            return Results.Ok(new { invoice.Id, invoice.InvoiceNumber,
                documentStatus = invoice.DocumentStatus.ToString(), documents = documents.Select(item => new
                {
                    item.Id, item.DocumentNumber, item.type, item.status, item.IssueDate,
                    item.Reason, item.Note, item.SignedAmount, item.CreatedByUserId, item.CreatedUtc,
                    actorName = ActorName(actorNames, item.CreatedByUserId)
                }) });
        });
        group.MapGet("/timeline", async (Guid zevId, Guid invoiceId, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var invoice = await ReadAllowedInvoiceAsync(zevId, invoiceId, access, database, cancellationToken);
            if (invoice is null) return Results.NotFound();
            var documents = await database.FinancialDocuments.AsNoTracking()
                .Where(item => item.ZevId == zevId && item.OriginalInvoiceId == invoiceId)
                .Select(item => new { item.Id, item.DocumentNumber, item.Type, item.SignedAmount,
                    item.IssueDate, item.CreatedUtc, item.CreatedByUserId }).ToListAsync(cancellationToken);
            var chargeIds = await database.InvoiceLines.AsNoTracking()
                .Where(item => item.InvoiceId == invoiceId).Select(item => item.ChargeId)
                .Concat(database.FinancialDocumentLines.AsNoTracking()
                    .Where(item => item.Document.OriginalInvoiceId == invoiceId)
                    .Select(item => item.ChargeId)).ToArrayAsync(cancellationToken);
            var payments = await database.PaymentAllocations.AsNoTracking()
                .Where(item => chargeIds.Contains(item.ChargeId) && item.Payment.Status == PaymentStatus.Posted)
                .Select(item => new { item.Id, item.PaymentId, item.FinancialDocumentId,
                    item.Amount, item.ReversalReason, item.CreatedUtc,
                    item.Payment.PaidOn, paymentCreatedUtc = item.Payment.CreatedUtc })
                .ToListAsync(cancellationToken);
            var paymentIds = payments.Select(item => item.PaymentId).Distinct().ToArray();
            var paymentAmounts = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId
                    && paymentIds.Contains(item.Id))
                .Select(item => new { item.Id, item.Amount, item.CurrencyCode })
                .ToDictionaryAsync(item => item.Id, item => item.Amount, cancellationToken);
            var detailRows = await database.PaymentAllocations.AsNoTracking().Where(item => item.ZevId == zevId
                    && paymentAmounts.Keys.Contains(item.PaymentId) && item.Charge.PersonId == invoice.PersonId)
                .Select(item => new { item.PaymentId, item.ChargeId, item.Amount,
                    unitCode = item.Charge.PropertyUnit.Code,
                    invoiceNumber = database.InvoiceLines.Where(line => line.ChargeId == item.ChargeId)
                        .Select(line => line.Invoice.InvoiceNumber).FirstOrDefault(),
                    description = database.InvoiceLines.Where(line => line.ChargeId == item.ChargeId)
                        .Select(line => line.DescriptionSnapshot).FirstOrDefault() })
                .ToListAsync(cancellationToken);
            var allocatedByPayment = await database.PaymentAllocations.AsNoTracking()
                .Where(item => item.ZevId == zevId && paymentAmounts.Keys.Contains(item.PaymentId))
                .GroupBy(item => item.PaymentId)
                .Select(group => new { PaymentId = group.Key, Amount = group.Sum(item => item.Amount) })
                .ToDictionaryAsync(item => item.PaymentId, item => item.Amount, cancellationToken);
            var documentById = documents.ToDictionary(item => item.Id);
            var allocationEvents = payments.Select(item =>
            {
                var related = item.FinancialDocumentId is Guid documentId
                    ? documentById.GetValueOrDefault(documentId) : null;
                return new TimelineAllocation(item.PaymentId, item.FinancialDocumentId, item.Amount,
                    item.PaidOn, item.paymentCreatedUtc, related?.IssueDate, related?.CreatedUtc);
            });
            var actorNames = await ActorNamesAsync(database, invoice.OrganizationId,
                documents.Select(item => item.CreatedByUserId).Append(invoice.IssuedByUserId), cancellationToken);
            var manualReversalIds = payments.Where(item => item.Amount < 0m && item.FinancialDocumentId is null)
                .Select(item => item.Id.ToString()).ToArray();
            var reversalActors = await database.AuditLogs.AsNoTracking().Where(item => item.ZevId == zevId
                    && item.EntityType == nameof(PaymentAllocation) && item.Action == "Added"
                    && manualReversalIds.Contains(item.EntityId))
                .Select(item => new { item.EntityId, item.ActorUserId }).ToListAsync(cancellationToken);
            var reversalNames = await ActorNamesAsync(database, invoice.OrganizationId,
                reversalActors.Select(item => item.ActorUserId), cancellationToken);
            var events = new List<DocumentTimelineEvent>
            {
                new(invoice.IssuedUtc, "Invoice", invoice.InvoiceNumber, invoice.TotalAmount,
                    invoice.Id, invoice.IssueDate, ActorName(actorNames, invoice.IssuedByUserId))
            };
            events.AddRange(documents.Select(item => new DocumentTimelineEvent(item.CreatedUtc,
                item.Type.ToString(), item.DocumentNumber, item.SignedAmount, item.Id, item.IssueDate,
                ActorName(actorNames, item.CreatedByUserId))));
            events.AddRange(InvoiceTimelineBuilder.PaymentEvents(allocationEvents).Select(item =>
            {
                if (item.Type == "Payment" && paymentAmounts.TryGetValue(item.SourceId, out var amount))
                {
                    var rows = detailRows.Where(row => row.PaymentId == item.SourceId).ToList();
                    var allocated = allocatedByPayment.GetValueOrDefault(item.SourceId);
                    return item with { Amount = amount, AppliedToInvoice = item.Amount,
                        CurrencyCode = invoice.Currency, Allocated = allocated, Credit = amount - allocated,
                        AllocationDetails = rows.Select(row => new TimelineAllocationDetail(row.ChargeId,
                            row.invoiceNumber, row.unitCode, row.description, row.Amount)).ToList() };
                }
                return item.FinancialDocumentId is Guid documentId
                    && documentById.TryGetValue(documentId, out var related)
                    ? item with { ActorName = ActorName(actorNames, related.CreatedByUserId) } : item;
            }));
            events.AddRange(payments.Where(item => item.Amount < 0m && item.FinancialDocumentId is null)
                .Select(item => new DocumentTimelineEvent(item.CreatedUtc, "ManualAllocationReversal",
                    null, -item.Amount, item.Id, DateOnly.FromDateTime(item.CreatedUtc.UtcDateTime),
                    ActorName(reversalNames, reversalActors.FirstOrDefault(actor => actor.EntityId == item.Id.ToString())
                        ?.ActorUserId))
                { CurrencyCode = invoice.Currency, Reason = item.ReversalReason }));
            return Results.Ok(events.OrderBy(item => item.OccurredUtc).ThenBy(item => item.Type));
        });
        var documentsGroup = api.MapGroup("/zevs/{zevId:guid}/finance/documents");
        documentsGroup.MapGet("/{documentId:guid}", async (Guid zevId, Guid documentId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var document = await ReadAllowedDocumentAsync(zevId, documentId, access, database, cancellationToken);
            if (document is null) return Results.NotFound();
            var actorNames = await ActorNamesAsync(database, document.OrganizationId,
                [document.CreatedByUserId], cancellationToken);
            return Results.Ok(DocumentView(document, ActorName(actorNames, document.CreatedByUserId)));
        });
        documentsGroup.MapGet("/{documentId:guid}.pdf", async (Guid zevId, Guid documentId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var document = await ReadAllowedDocumentAsync(zevId, documentId, access, database, cancellationToken);
            if (document is null) return Results.NotFound();
            return Results.File(RenderDocumentPdf(document), "application/pdf",
                $"dokument-{document.DocumentNumber}.pdf");
        });
        return api;
    }

    private static async Task<Invoice?> ReadAllowedInvoiceAsync(Guid zevId, Guid invoiceId,
        TenantAccess access, ZevDbContext database, CancellationToken cancellationToken)
    {
        var invoice = await database.Invoices.AsNoTracking().SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Id == invoiceId, cancellationToken);
        return invoice is not null && await InvoiceEndpoints.CanReadInvoiceAsync(access, zevId,
            invoice.PersonId, invoice.Period, cancellationToken) ? invoice : null;
    }

    private static async Task<FinancialDocument?> ReadAllowedDocumentAsync(Guid zevId, Guid documentId,
        TenantAccess access, ZevDbContext database, CancellationToken cancellationToken)
    {
        var document = await database.FinancialDocuments.AsNoTracking()
            .Include(item => item.Lines).Include(item => item.OriginalInvoice)
            .SingleOrDefaultAsync(item => item.ZevId == zevId && item.Id == documentId, cancellationToken);
        return document is not null && await InvoiceEndpoints.CanReadInvoiceAsync(access, zevId,
            document.OriginalInvoice.PersonId, document.OriginalInvoice.Period, cancellationToken)
            ? document : null;
    }

    private static object DocumentView(FinancialDocument document, string? actorName) => new
    {
        document.Id, document.OriginalInvoiceId, document.OriginalInvoiceNumberSnapshot,
        document.DocumentNumber, type = document.Type.ToString(), status = document.DocumentStatus.ToString(),
        document.IssueDate,
        document.Reason, document.Note, document.SignedAmount, document.Currency,
        document.ZevNameSnapshot, document.PersonNameSnapshot, document.UnitCodeSnapshot,
        document.CreatedByUserId, actorName, document.CreatedUtc,
        lines = document.Lines.OrderBy(item => item.Id).Select(item => new
        {
            item.Id, item.OriginalInvoiceLineId, item.FundId, item.FundNameSnapshot,
            item.DescriptionSnapshot, item.SignedAmount
        })
    };

    private static async Task<Dictionary<Guid, string>> ActorNamesAsync(ZevDbContext database,
        Guid organizationId, IEnumerable<Guid?> actorIds, CancellationToken cancellationToken)
    {
        var ids = actorIds.Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var users = await database.Users.AsNoTracking().Where(item => ids.Contains(item.Id))
            .Select(item => new { item.Id, item.Email }).ToListAsync(cancellationToken);
        var people = await database.People.AsNoTracking().Where(item => item.OrganizationId == organizationId
                && item.UserLinkStatus == UserPersonLinkStatus.Verified
                && item.UserId.HasValue && ids.Contains(item.UserId.Value))
            .OrderBy(item => item.Id).Select(item => new { item.UserId, item.DisplayName })
            .ToListAsync(cancellationToken);
        var names = users.ToDictionary(item => item.Id, item => item.Email);
        foreach (var person in people.Where(item => !string.IsNullOrWhiteSpace(item.DisplayName)))
            names[person.UserId!.Value] = person.DisplayName;
        return names;
    }

    private static string? ActorName(IReadOnlyDictionary<Guid, string> names, Guid? actorId) =>
        actorId is Guid id ? names.GetValueOrDefault(id) ?? "Nepoznat korisnik" : null;

    private static byte[] RenderDocumentPdf(FinancialDocument document)
    {
        var title = document.Type switch
        {
            FinancialDocumentType.Storno => "STORNO RAČUNA",
            FinancialDocumentType.Credit => "KOREKTIVNI DOKUMENT – UMANJENJE",
            _ => "KOREKTIVNI DOKUMENT – DODATNO ZADUŽENJE"
        };
        var lines = new List<string>
        {
            title, $"Broj: {document.DocumentNumber}",
            $"Datum: {document.IssueDate:dd.MM.yyyy}",
            $"Originalni račun: {document.OriginalInvoiceNumberSnapshot} od {document.OriginalInvoice.IssueDate:dd.MM.yyyy}",
            $"ZEV: {document.ZevNameSnapshot}",
            $"Primalac: {document.PersonNameSnapshot}",
            $"Jedinica: {document.UnitCodeSnapshot}",
            "Stavke:"
        };
        lines.AddRange(document.Lines.OrderBy(item => item.Id).Select(item =>
            $"{item.DescriptionSnapshot} / {item.FundNameSnapshot}: {item.SignedAmount:0.00} {document.Currency}"));
        lines.Add($"Ukupna korekcija: {document.SignedAmount:0.00} {document.Currency}");
        lines.Add($"Razlog: {document.Reason}");
        if (!string.IsNullOrWhiteSpace(document.Note)) lines.Add($"Napomena: {document.Note}");
        return SimplePdfWriter.Write(lines);
    }
}

public sealed record DocumentTimelineEvent(DateTimeOffset OccurredUtc, string Type,
    string? Number, decimal Amount, Guid SourceId, DateOnly Date, string? ActorName = null,
    Guid? FinancialDocumentId = null)
{
    public decimal? Allocated { get; init; }
    public decimal? Credit { get; init; }
    public decimal? AppliedToInvoice { get; init; }
    public string? CurrencyCode { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<TimelineAllocationDetail>? AllocationDetails { get; init; }
}

public sealed record TimelineAllocationDetail(Guid ChargeId, string? InvoiceNumber, string UnitCode,
    string? Description, decimal Amount);
