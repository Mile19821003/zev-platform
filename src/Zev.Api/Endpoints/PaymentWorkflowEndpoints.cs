using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Payments;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class PaymentWorkflowEndpoints
{
    public static RouteGroupBuilder MapPaymentWorkflowEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/lookup", async (Guid zevId, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.View", cancellationToken) is { } denied) return denied;
            var funds = await database.Funds.AsNoTracking().Where(item => item.ZevId == zevId && item.IsActive)
                .OrderBy(item => item.Name).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken);
            var accounts = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId)
                .OrderBy(item => item.AccountNumber).Select(item => new { item.Id, item.AccountNumber })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { funds, accounts });
        });

        group.MapGet("/credit-policy", async (Guid zevId, TenantAccess access, ZevDbContext database,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.View", cancellationToken) is { } denied) return denied;
            var zev = await database.Zevs.AsNoTracking().Where(item => item.Id == zevId)
                .Select(item => new { item.AutoApplyPaymentCredit, item.RowVersion })
                .SingleOrDefaultAsync(cancellationToken);
            return zev is null ? Results.NotFound() : Results.Ok(new { zev.AutoApplyPaymentCredit,
                allowManualCash = configuration.GetValue<bool>("Payments:AllowManualCash"),
                rowVersion = Convert.ToBase64String(zev.RowVersion) });
        });

        group.MapPut("/credit-policy", async (Guid zevId, CreditPolicyCommand request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.ReconcileAutomatic", cancellationToken) is { } denied)
                return denied;
            var zev = await database.Zevs.SingleOrDefaultAsync(item => item.Id == zevId, cancellationToken);
            if (zev is null) return Results.NotFound();
            if (Convert.ToBase64String(zev.RowVersion) != request.RowVersion) return Results.Conflict();
            zev.AutoApplyPaymentCredit = request.Enabled;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { zev.AutoApplyPaymentCredit,
                    rowVersion = Convert.ToBase64String(zev.RowVersion) });
            }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
        });

        group.MapGet("/people", async (Guid zevId, string? search, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.View", cancellationToken) is { } denied) return denied;
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.People.AsNoTracking().Where(item => item.IsActive
                && (database.Ownerships.Any(ownership => ownership.ZevId == zevId && ownership.PersonId == item.Id)
                    || database.Charges.Any(charge => charge.ZevId == zevId && charge.PersonId == item.Id)));
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.DisplayName.Contains(search.Trim()));
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.DisplayName).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.DisplayName }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapGet("/units", async (Guid zevId, Guid personId, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.View", cancellationToken) is { } denied) return denied;
            var units = await database.Ownerships.AsNoTracking().Where(item => item.ZevId == zevId
                && item.PersonId == personId && item.ValidFrom <= DateOnly.FromDateTime(DateTime.UtcNow)
                && (item.ValidTo == null || item.ValidTo > DateOnly.FromDateTime(DateTime.UtcNow)))
                .Select(item => new { id = item.PropertyUnitId, code = item.PropertyUnit.Code })
                .Distinct().OrderBy(item => item.code).ToListAsync(cancellationToken);
            return Results.Ok(units);
        });

        group.MapPost("/manual", async (Guid zevId, ManualPaymentInput request, ClaimsPrincipal user,
            TenantAccess access, PaymentWorkflowService workflow, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.CreateManual", cancellationToken) is { } denied) return denied;
            var actorId = Guid.TryParse(user.FindFirstValue("sub"), out var parsed) ? parsed : (Guid?)null;
            var result = await workflow.CreateManualAsync(zevId, request, actorId, cancellationToken);
            return result.Success ? Results.Ok(new { id = result.PaymentId, result.AlreadyExists })
                : Results.Conflict(new { error = result.Error });
        });

        group.MapPost("/{paymentId:guid}/allocations", async (Guid zevId, Guid paymentId,
            PaymentAllocationCommand request, TenantAccess access, PaymentWorkflowService workflow, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.ReconcileManual", cancellationToken) is { } denied) return denied;
            var actorId = Guid.TryParse(user.FindFirstValue("sub"), out var parsed) ? parsed : (Guid?)null;
            var result = await workflow.AllocateAsync(zevId, paymentId, request.RequestId,
                request.Allocations ?? [], cancellationToken, actorId);
            return result.Success ? Results.Ok(new { id = result.PaymentId, result.AlreadyExists })
                : Results.Conflict(new { error = result.Error });
        });

        group.MapGet("/{paymentId:guid}/automatic-preview", async (Guid zevId, Guid paymentId,
            Guid? propertyUnitId, TenantAccess access, PaymentWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.View", cancellationToken) is { } denied) return denied;
            var preview = await workflow.PreviewAutomaticAsync(zevId, paymentId, propertyUnitId, cancellationToken);
            return preview.Error is null ? Results.Ok(preview) : Results.Conflict(new { error = preview.Error });
        });

        group.MapPost("/{paymentId:guid}/automatic", async (Guid zevId, Guid paymentId,
            AutoAllocationCommand request, TenantAccess access, PaymentWorkflowService workflow,
            ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.ReconcileManual", cancellationToken) is { } denied) return denied;
            if (request.RequestId == Guid.Empty) return Results.BadRequest(new { error = "Request ID is required." });
            var prior = await database.PaymentAllocations.AsNoTracking().Where(item => item.ZevId == zevId
                && item.PaymentId == paymentId && item.OperationId == request.RequestId)
                .Select(item => item.Charge.PropertyUnitId).ToListAsync(cancellationToken);
            if (prior.Count > 0) return prior.All(unit => request.PropertyUnitId is null || unit == request.PropertyUnitId)
                ? Results.Ok(new { id = paymentId, alreadyExists = true })
                : Results.Conflict(new { error = "Request ID was used for another allocation." });
            var preview = await workflow.PreviewAutomaticAsync(zevId, paymentId, request.PropertyUnitId, cancellationToken);
            if (preview.Error is not null || preview.Lines.Count == 0)
                return Results.Conflict(new { error = preview.Error ?? "No open charges were found." });
            var result = await workflow.AllocateAsync(zevId, paymentId, request.RequestId,
                preview.Lines.Select(item => new PaymentAllocationInput(item.ChargeId, item.Amount)).ToList(),
                cancellationToken);
            return result.Success ? Results.Ok(new { id = result.PaymentId, result.AlreadyExists })
                : Results.Conflict(new { error = result.Error });
        });

        group.MapPost("/{paymentId:guid}/identify", async (Guid zevId, Guid paymentId,
            IdentifyPaymentCommand request, TenantAccess access, PaymentWorkflowService workflow, ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.ReconcileManual", cancellationToken) is { } denied) return denied;
            var actorId = Guid.TryParse(user.FindFirstValue("sub"), out var parsed) ? parsed : (Guid?)null;
            var result = await workflow.IdentifyAsync(zevId, paymentId, request.PersonId, cancellationToken, actorId);
            return result.Success ? Results.Ok(new { id = result.PaymentId, result.AlreadyExists })
                : Results.Conflict(new { error = result.Error });
        });

        group.MapPost("/{paymentId:guid}/change-payer", async (Guid zevId, Guid paymentId,
            ChangePayerCommand request, TenantAccess access, PaymentWorkflowService workflow,
            ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.ChangePayer", cancellationToken) is { } denied) return denied;
            var actorId = Guid.TryParse(user.FindFirstValue("sub"), out var parsed) ? parsed : (Guid?)null;
            var result = await workflow.ChangePayerAsync(zevId, paymentId, request.PersonId,
                request.Reason, request.Allocations, actorId, cancellationToken);
            return result.Success ? Results.Ok(new { id = result.PaymentId })
                : Results.Conflict(new { error = result.Error });
        });

        group.MapPost("/{paymentId:guid}/reverse", async (Guid zevId, Guid paymentId,
            ReversePaymentCommand request, TenantAccess access, PaymentWorkflowService workflow,
            ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.Reverse", cancellationToken) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
                return Results.BadRequest(new { error = "Reason is required (max 500 characters)." });
            var actorId = Guid.TryParse(user.FindFirstValue("sub"), out var parsed) ? parsed : (Guid?)null;
            var result = await workflow.ReverseManualAsync(zevId, paymentId, request.Reason, actorId, cancellationToken);
            return result.Status switch
            {
                ManualPaymentReversalStatus.Reversed => Results.Ok(new { id = paymentId, alreadyExists = false }),
                ManualPaymentReversalStatus.AlreadyReversed => Results.Ok(new { id = paymentId, alreadyExists = true }),
                ManualPaymentReversalStatus.Invalid => Results.BadRequest(new { error = result.Error }),
                ManualPaymentReversalStatus.NotFound => Results.NotFound(),
                _ => Results.Conflict(new { error = result.Error }),
            };
        });

        group.MapPost("/allocations/{allocationId:guid}/reverse", async (Guid zevId, Guid allocationId,
            ReverseAllocationCommand request, TenantAccess access, PaymentWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.ReverseAllocation", cancellationToken) is { } denied) return denied;
            var result = await workflow.ReverseAllocationAsync(zevId, allocationId, request.Reason, cancellationToken);
            return result.Success ? Results.Ok(new { id = result.PaymentId, result.AlreadyExists })
                : Results.Conflict(new { error = result.Error });
        });

        group.MapGet("/open-charges", async (Guid zevId, Guid? personId, Guid? propertyUnitId, Guid? fundId,
            string? search, int? page, int? pageSize, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.View", cancellationToken) is { } denied) return denied;
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Charges.AsNoTracking().Where(item => item.ZevId == zevId && item.Amount > 0m);
            if (personId is Guid owner) query = query.Where(item => item.PersonId == owner);
            if (propertyUnitId is Guid unit) query = query.Where(item => item.PropertyUnitId == unit);
            if (fundId is Guid fund) query = query.Where(item => item.FundId == fund);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(item => item.Reference.Contains(term) || item.Person.DisplayName.Contains(term)
                    || item.PropertyUnit.Code.Contains(term)
                    || database.InvoiceLines.Any(line => line.ChargeId == item.Id
                        && line.Invoice.InvoiceNumber.Contains(term)));
            }
            var projected = query.Select(item => new { item.Id, item.PropertyUnitId, item.PersonId, item.FundId,
                personName = item.Person.DisplayName, unitCode = item.PropertyUnit.Code, item.Period,
                item.DueDate, item.Reference, item.Amount,
                currencyCode = FinancialReadModel.Charges(database, zevId, null)
                    .Where(fact => fact.Id == item.Id).Select(fact => fact.CurrencyCode).FirstOrDefault(),
                invoiceNumber = database.InvoiceLines.Where(line => line.ChargeId == item.Id)
                    .Select(line => line.Invoice.InvoiceNumber).FirstOrDefault(),
                reduced = database.Charges.Where(reversal => reversal.ReversesChargeId == item.Id)
                    .Sum(reversal => (decimal?)reversal.Amount) ?? 0m,
                paid = database.PaymentAllocations.Where(allocation => allocation.ChargeId == item.Id
                    && allocation.Payment.Status == PaymentStatus.Posted)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m });
            var openQuery = projected.Where(item => item.Amount + item.reduced - item.paid > 0m);
            var totalCount = await openQuery.CountAsync(cancellationToken);
            var rows = await openQuery.OrderBy(item => item.DueDate).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount,
                items = rows.Select(item => new { item.Id, item.PropertyUnitId, item.PersonId, item.FundId,
                    item.personName, item.unitCode, item.Period, item.DueDate, item.Reference,
                    item.invoiceNumber, item.currencyCode,
                    open = FinancialReadModel.CalculateChargeOpen(item.Amount + item.reduced,
                        item.paid) }) });
        });

        group.MapGet("/bank-transactions", async (Guid zevId, Guid? bankAccountId, DateOnly? from,
            DateOnly? to, BankTransactionStatus? status, Guid? personId, Guid? propertyUnitId,
            string? reference, decimal? amount,
            string? search, int? page, int? pageSize, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.View", cancellationToken) is { } denied) return denied;
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.BankTransactions.AsNoTracking().Where(item => item.ZevId == zevId);
            if (bankAccountId is Guid account) query = query.Where(item => item.Import.BankAccountId == account);
            if (from is DateOnly start) query = query.Where(item => item.BookingDate >= start);
            if (to is DateOnly end) query = query.Where(item => item.BookingDate <= end);
            if (status is BankTransactionStatus selected) query = query.Where(item => item.Status == selected);
            if (personId is Guid person) query = query.Where(item => database.Payments.Any(payment =>
                payment.BankTransactionId == item.Id && payment.PersonId == person));
            if (propertyUnitId is Guid unit) query = query.Where(item => database.PaymentAllocations.Any(allocation =>
                allocation.Payment.BankTransactionId == item.Id && allocation.Charge.PropertyUnitId == unit));
            if (reference is not null) query = query.Where(item => item.Reference != null && item.Reference.Contains(reference));
            if (amount is decimal selectedAmount) query = query.Where(item => item.Amount == selectedAmount);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item =>
                item.Counterparty != null && item.Counterparty.Contains(search)
                || item.Description != null && item.Description.Contains(search));
            var totalCount = await query.CountAsync(cancellationToken);
            var rows = await query.OrderByDescending(item => item.BookingDate).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.ImportId, item.BookingDate, item.Amount,
                    item.Reference, item.Counterparty, item.Description, status = item.Status.ToString(),
                    currencyCode = item.Import.BankAccount.CurrencyCode,
                    bankAccountId = item.Import.BankAccountId, accountNumber = item.Import.BankAccount.AccountNumber,
                    payment = database.Payments.Where(payment => payment.BankTransactionId == item.Id)
                        .Select(payment => new { payment.Id, payment.PersonId,
                            personName = payment.PersonId == null ? null : database.People.Where(person =>
                                person.Id == payment.PersonId).Select(person => person.DisplayName).FirstOrDefault(),
                            allocated = database.PaymentAllocations.Where(allocation => allocation.PaymentId == payment.Id)
                                .Sum(allocation => (decimal?)allocation.Amount) ?? 0m }).FirstOrDefault(),
                    rule = database.ReconciliationAttempts.Where(attempt => attempt.BankTransactionId == item.Id)
                        .OrderByDescending(attempt => attempt.CreatedUtc).Select(attempt => attempt.Rule).FirstOrDefault() })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount,
                items = rows.Select(item => new { item.Id, item.ImportId, item.BookingDate, item.Amount,
                    item.Reference, item.Counterparty, item.Description, item.status,
                    item.bankAccountId, item.accountNumber, item.currencyCode, item.payment, item.rule,
                    allocationStatus = item.status == nameof(BankTransactionStatus.Returned) ? "REVERSED"
                        : item.payment is null || item.payment.PersonId is null ? "MANUAL_REVIEW"
                        : item.payment.allocated >= item.Amount ? "FULLY_ALLOCATED"
                        : item.payment.allocated > 0m ? "PARTIALLY_ALLOCATED_CREDIT_REMAINS"
                        : item.status == nameof(BankTransactionStatus.ManualReview) ? "MANUAL_REVIEW"
                        : "IDENTIFIED_UNALLOCATED",
                    remaining = item.Amount > 0m ? item.Amount - (item.payment?.allocated ?? 0m) : 0m }) });
        });

        group.MapGet("/{paymentId:guid}/details", async (Guid zevId, Guid paymentId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Payments.View", cancellationToken) is { } denied) return denied;
            var payment = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId
                && item.Id == paymentId).Select(item => new { item.Id, item.PersonId, item.FundId,
                item.Amount, item.PaidOn, item.CurrencyCode, item.BankTransactionId,
                sourceType = item.SourceType.ToString(), status = item.Status.ToString(),
                personName = database.People.Where(person => person.Id == item.PersonId)
                    .Select(person => person.DisplayName).FirstOrDefault(),
                    item.ManualReason, item.ManualMethod, item.ManualReference, item.ManualDescription,
                    item.CreatedByUserId, item.CreatedUtc }).SingleOrDefaultAsync(cancellationToken);
            if (payment is null) return Results.NotFound();
            var rows = await database.PaymentAllocations.AsNoTracking().Where(item => item.ZevId == zevId
                && item.PaymentId == paymentId).OrderBy(item => item.CreatedUtc).ThenBy(item => item.Id)
                .Select(item => new { item.Id, item.ChargeId, item.Amount, item.ReversesAllocationId,
                    item.FinancialDocumentId, item.ReversalReason, item.CreatedUtc,
                    item.Charge.PropertyUnitId, unitCode = item.Charge.PropertyUnit.Code,
                    item.Charge.Period, item.Charge.Reference,
                    invoiceNumber = database.InvoiceLines.Where(line => line.ChargeId == item.ChargeId)
                        .Select(line => line.Invoice.InvoiceNumber).FirstOrDefault(),
                    description = database.InvoiceLines.Where(line => line.ChargeId == item.ChargeId)
                        .Select(line => line.DescriptionSnapshot).FirstOrDefault(),
                    releaseInvoiceNumber = database.FinancialDocuments.Where(document =>
                            document.Id == item.FinancialDocumentId)
                        .Select(document => document.OriginalInvoiceNumberSnapshot).FirstOrDefault() })
                .ToListAsync(cancellationToken);
            var allocated = rows.Sum(item => item.Amount);
            var bank = payment.BankTransactionId is Guid bankId
                ? await database.BankTransactions.AsNoTracking().Where(item => item.Id == bankId
                    && item.ZevId == zevId).Select(item => new { item.BookingDate, item.Reference,
                        item.Counterparty, item.Description, status = item.Status.ToString(),
                        rule = database.ReconciliationAttempts.Where(attempt => attempt.BankTransactionId == item.Id)
                            .OrderByDescending(attempt => attempt.CreatedUtc).Select(attempt => attempt.Rule).FirstOrDefault() })
                    .SingleOrDefaultAsync(cancellationToken) : null;
            var identificationHistory = await database.PaymentIdentificationHistories.AsNoTracking()
                .Where(item => item.ZevId == zevId && item.PaymentId == paymentId)
                .OrderBy(item => item.ChangedUtc).ThenBy(item => item.Id)
                .Select(item => new { item.Id, item.PreviousPersonId, item.NewPersonId,
                    previousPersonName = database.People.Where(person => person.Id == item.PreviousPersonId)
                        .Select(person => person.DisplayName).FirstOrDefault(),
                    newPersonName = database.People.Where(person => person.Id == item.NewPersonId)
                        .Select(person => person.DisplayName).FirstOrDefault(),
                    item.Reason, item.ChangedByUserId,
                    actorName = database.People.Where(person => person.UserId == item.ChangedByUserId)
                        .Select(person => person.DisplayName).FirstOrDefault()
                        ?? database.Users.Where(user => user.Id == item.ChangedByUserId)
                            .Select(user => user.Email).FirstOrDefault(),
                    item.ChangedUtc })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { payment, allocated, credit = payment.status == "Posted"
                ? payment.Amount - allocated : 0m, bankTransaction = bank, allocations = rows,
                identificationHistory });
        });

        return group;
    }
}

public sealed record PaymentAllocationCommand(Guid RequestId, IReadOnlyList<PaymentAllocationInput>? Allocations);
public sealed record AutoAllocationCommand(Guid RequestId, Guid? PropertyUnitId);
public sealed record IdentifyPaymentCommand(Guid PersonId);
public sealed record ChangePayerCommand(Guid PersonId, string Reason,
    IReadOnlyList<PaymentAllocationInput>? Allocations);
public sealed record ReverseAllocationCommand(string Reason);
public sealed record ReversePaymentCommand(string? Reason);
public sealed record CreditPolicyCommand(bool Enabled, string RowVersion);
