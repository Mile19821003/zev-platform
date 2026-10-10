using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Payments;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/payments");
        group.MapPaymentWorkflowEndpoints();

        group.MapGet("/accounts", async (Guid zevId, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "BankAccounts.View", cancellationToken)) return Results.NotFound();
            var accounts = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId)
                .OrderBy(item => item.AccountNumber).Select(item => new { item.Id, item.FundId, item.AccountNumber,
                    item.BankName, item.CurrencyCode, item.IsActive }).ToListAsync(cancellationToken);
            return Results.Ok(accounts);
        });

        group.MapPost("/accounts", async (Guid zevId, CreateBankAccountRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "BankAccounts.Create", cancellationToken)) return Results.NotFound();
            var accountNumber = AdminValidation.TransactionAccount(request.AccountNumber);
            if (accountNumber is null || !AdminValidation.Optional(request.BankName, 120)
                || request.CurrencyCode?.Length != 3 || !request.CurrencyCode.All(char.IsLetter))
                return Results.BadRequest(new { error = "Transakcijski račun mora imati tačno 16 cifara (BiH standard)." });
            var fund = await database.Funds.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.FundId
                && item.ZevId == zevId && item.IsActive, cancellationToken);
            if (fund is null) return Results.NotFound();
            var account = new BankAccount
            {
                OrganizationId = fund.OrganizationId, ZevId = zevId, FundId = fund.Id,
                AccountNumber = accountNumber, BankName = AdminValidation.Clean(request.BankName),
                CurrencyCode = request.CurrencyCode.ToUpperInvariant()
            };
            database.BankAccounts.Add(account);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Created($"/api/zevs/{zevId}/payments/accounts", new { account.Id, account.AccountNumber });
            }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapPost("/statements/preview", async (Guid zevId, ImportStatementRequest request, TenantAccess access,
            PaymentReconciliationEngine engine, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "BankStatements.Preview", cancellationToken)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.ContentBase64) || request.ContentBase64.Length > 7_000_000)
                return Results.BadRequest();
            byte[] content;
            try { content = Convert.FromBase64String(request.ContentBase64); }
            catch (FormatException) { return Results.BadRequest(); }
            var result = await engine.PreviewAsync(zevId, request.BankAccountId, request.FileName,
                request.Format, content, cancellationToken);
            var currencyCode = await database.BankAccounts.AsNoTracking()
                .Where(account => account.ZevId == zevId && account.Id == request.BankAccountId)
                .Select(account => account.CurrencyCode).SingleOrDefaultAsync(cancellationToken);
            return result.Rows is null ? Results.BadRequest(new { error = result.Error })
                : Results.Ok(new { transactionCount = result.Rows.Count, result.IsDuplicate,
                    items = result.Rows.Take(50), truncated = result.Rows.Count > 50, currencyCode });
        }).RequireRateLimiting("upload");

        group.MapPost("/statements", async (Guid zevId, ImportStatementRequest request, TenantAccess access,
            PaymentReconciliationEngine engine, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "BankStatements.Import", cancellationToken)
                || !await access.HasPermissionAsync(zevId, "BankStatements.Post", cancellationToken)
                || !await access.HasPermissionAsync(zevId, "Payments.ReconcileAutomatic", cancellationToken))
                return Results.NotFound();
            byte[] content;
            if (string.IsNullOrWhiteSpace(request.ContentBase64) || request.ContentBase64.Length > 7_000_000)
                return Results.BadRequest();
            try { content = Convert.FromBase64String(request.ContentBase64); }
            catch (FormatException) { return Results.BadRequest(); }
            var result = await engine.ImportAsync(zevId, request.BankAccountId, request.FileName,
                request.Format, content, cancellationToken);
            if (result.Import is null) return result.Error?.Contains("already imported") == true
                ? Results.Conflict(new { error = result.Error }) : Results.BadRequest(new { error = result.Error });
            return Results.Created($"/api/zevs/{zevId}/payments/statements/{result.Import.Id}",
                new { result.Import.Id, result.Import.TransactionCount, result.MatchedCount,
                    status = result.Import.Status.ToString() });
        }).RequireRateLimiting("upload");

        group.MapGet("/statements", async (Guid zevId, int? page, int? pageSize, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "BankStatements.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.BankStatementImports.AsNoTracking().Where(item => item.ZevId == zevId);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.ImportedUtc).Skip((currentPage - 1) * currentSize)
                .Take(currentSize).Select(item => new { item.Id, item.BankAccountId, item.OriginalFileName,
                    item.ChecksumSha256, item.Format, status = item.Status.ToString(), item.TransactionCount,
                    item.ImportedUtc, item.ImportedByUserId }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapGet("/reconciliation-queue", async (Guid zevId, bool? returnsOnly,
            int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Payments.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.BankTransactions.AsNoTracking().Where(item => item.ZevId == zevId
                && (item.Status == BankTransactionStatus.ManualReview
                    || item.Status == BankTransactionStatus.PartiallyMatched));
            if (returnsOnly == true) query = query.Where(item => item.Amount < 0m);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.BookingDate).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.ImportId, item.BookingDate, item.Amount,
                    item.Reference, item.Counterparty, item.Description,
                    currencyCode = item.Import.BankAccount.CurrencyCode, status = item.Status.ToString(),
                    paymentId = database.Payments.Where(payment => payment.BankTransactionId == item.Id)
                        .Select(payment => (Guid?)payment.Id).FirstOrDefault() })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("/bank-transactions/{transactionId:guid}/allocate", async (Guid zevId, Guid transactionId,
            ManualAllocationRequest request, TenantAccess access, PaymentWorkflowService workflow,
            ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Payments.ReconcileManual", cancellationToken)) return Results.NotFound();
            if (request.Allocations is null) return Results.BadRequest();
            var paymentId = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId
                && item.BankTransactionId == transactionId).Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (paymentId is null) return Results.Conflict(new { error = "Bank payment was not found." });
            var canonical = string.Join("|", request.Allocations.OrderBy(item => item.ChargeId)
                .Select(item => $"{item.ChargeId:N}:{item.Amount.ToString(CultureInfo.InvariantCulture)}"));
            var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{transactionId:N}|{canonical}"));
            var operationId = request.RequestId ?? new Guid(hash.AsSpan(0, 16));
            var result = await workflow.AllocateAsync(zevId, paymentId.Value, operationId,
                request.Allocations.Select(item => new PaymentAllocationInput(item.ChargeId, item.Amount)).ToList(),
                cancellationToken);
            return result.Success ? Results.Ok(new { status = "Matched", result.AlreadyExists })
                : Results.Conflict(new { error = result.Error });
        });

        group.MapPost("/bank-transactions/{transactionId:guid}/return", async (Guid zevId, Guid transactionId,
            BankReturnRequest request, TenantAccess access, PaymentReconciliationEngine engine,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Payments.Reverse", cancellationToken)) return Results.NotFound();
            var result = await engine.ReverseFullPaymentAsync(zevId, transactionId, request.PaymentId,
                request.Reason, cancellationToken);
            return result.Success ? Results.Ok(new { status = "Returned" })
                : Results.Conflict(new { error = result.Error });
        });

        group.MapGet("/payments", async (Guid zevId, Guid? personId, Guid? propertyUnitId,
            Guid? bankAccountId, DateOnly? from, DateOnly? to, decimal? amount, string? reference,
            string? search, string? filterStatus, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Payments.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Payments.AsNoTracking().Where(item => item.ZevId == zevId);
            if (personId is Guid selectedPerson) query = query.Where(item => item.PersonId == selectedPerson);
            if (propertyUnitId is Guid unit) query = query.Where(item => item.ManualUnitId == unit
                || database.PaymentAllocations.Any(allocation => allocation.PaymentId == item.Id
                    && allocation.Charge.PropertyUnitId == unit));
            if (bankAccountId is Guid account) query = query.Where(item => item.BankTransaction != null
                && item.BankTransaction.Import.BankAccountId == account);
            if (from is DateOnly start) query = query.Where(item => item.PaidOn >= start);
            if (to is DateOnly end) query = query.Where(item => item.PaidOn <= end);
            if (amount is decimal selectedAmount) query = query.Where(item => item.Amount == selectedAmount);
            if (!string.IsNullOrWhiteSpace(reference)) query = query.Where(item =>
                item.ManualReference != null && item.ManualReference.Contains(reference)
                || item.BankTransaction != null && item.BankTransaction.Reference != null
                    && item.BankTransaction.Reference.Contains(reference));
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item =>
                item.ManualDescription != null && item.ManualDescription.Contains(search)
                || item.BankTransaction != null && (item.BankTransaction.Counterparty != null
                    && item.BankTransaction.Counterparty.Contains(search)
                    || item.BankTransaction.Description != null && item.BankTransaction.Description.Contains(search))
                || database.People.Any(person => person.Id == item.PersonId && person.DisplayName.Contains(search)));
            var projected = query.Select(item => new { item.Id, item.PersonId, item.FundId, item.Amount,
                item.Status, item.SourceType,
                item.CurrencyCode, item.PaidOn, item.BankTransactionId, item.ManualUnitId,
                reference = item.BankTransaction == null ? item.ManualReference : item.BankTransaction.Reference,
                counterparty = item.BankTransaction == null ? null : item.BankTransaction.Counterparty,
                description = item.BankTransaction == null ? item.ManualDescription : item.BankTransaction.Description,
                bankStatus = item.BankTransaction == null ? (BankTransactionStatus?)null : item.BankTransaction.Status,
                matchRule = item.BankTransactionId == null ? null : database.ReconciliationAttempts
                    .Where(attempt => attempt.BankTransactionId == item.BankTransactionId
                        && attempt.PaymentId == item.Id)
                    .OrderBy(attempt => attempt.CreatedUtc).Select(attempt => attempt.Rule).FirstOrDefault(),
                bankAccountId = item.BankTransaction == null ? (Guid?)null : item.BankTransaction.Import.BankAccountId,
                personName = database.People.Where(person => person.Id == item.PersonId)
                    .Select(person => person.DisplayName).FirstOrDefault(),
                allocated = database.PaymentAllocations.Where(allocation => allocation.PaymentId == item.Id)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m });
            if (!string.IsNullOrWhiteSpace(filterStatus)) projected = filterStatus switch
            {
                "Unrecognized" => projected.Where(item => item.PersonId == null && item.Status == PaymentStatus.Posted),
                "Review" => projected.Where(item => item.bankStatus == BankTransactionStatus.ManualReview
                    || item.bankStatus == BankTransactionStatus.PartiallyMatched),
                "Partial" => projected.Where(item => item.Status == PaymentStatus.Posted
                    && item.allocated > 0m && item.allocated < item.Amount),
                "Credit" => projected.Where(item => item.Status == PaymentStatus.Posted
                    && item.Amount > item.allocated && item.PersonId != null),
                "Allocated" => projected.Where(item => item.allocated == item.Amount && item.Status == PaymentStatus.Posted),
                "Reversed" => projected.Where(item => item.Status == PaymentStatus.Reversed),
                "Recognized" => projected.Where(item => item.PersonId != null
                    && item.Status == PaymentStatus.Posted && item.bankStatus == BankTransactionStatus.Matched
                    && (item.matchRule == "EXACT_REFERENCE_V1"
                        || item.matchRule == "ACCOUNT_REFERENCE_OLDEST_DUE_V1")),
                _ => projected
            };
            var totalCount = await projected.CountAsync(cancellationToken);
            var items = await projected.OrderByDescending(item => item.PaidOn).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount,
                items = items.Select(item => new { item.Id, item.PersonId, item.personName, item.FundId,
                    item.Amount, status = item.Status.ToString(), sourceType = item.SourceType.ToString(),
                    item.CurrencyCode, item.PaidOn, item.BankTransactionId, item.bankAccountId,
                    bankStatus = item.bankStatus?.ToString(), item.matchRule, item.ManualUnitId,
                    item.reference, item.counterparty, item.description, item.allocated,
                    allocationStatus = PaymentAllocationStatus.Derive(item.Amount, item.allocated,
                        item.PersonId, item.Status, item.bankStatus),
                    unallocated = item.Status == PaymentStatus.Posted ? item.Amount - item.allocated : 0m }) });
        });

        group.MapGet("/export", async (Guid zevId, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Payments.View", cancellationToken)
                || !await access.HasPermissionAsync(zevId, "Payments.Export", cancellationToken)) return Results.NotFound();
            var payments = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId)
                .OrderByDescending(item => item.PaidOn).ThenBy(item => item.Id).Take(5000)
                .Select(item => new { item.Id, item.PersonId, item.PaidOn, item.Amount, item.CurrencyCode,
                    item.Status }).ToListAsync(cancellationToken);
            var csv = new StringBuilder("Id;PersonId;PaidOn;Amount;Currency;Status\r\n");
            foreach (var payment in payments)
                csv.Append(payment.Id).Append(';').Append(payment.PersonId).Append(';')
                    .Append(payment.PaidOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(';')
                    .Append(payment.Amount.ToString(CultureInfo.InvariantCulture)).Append(';')
                    .Append(payment.CurrencyCode).Append(';').Append(payment.Status).Append("\r\n");
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "payments.csv");
        });

        group.MapGet("/credits", async (Guid zevId, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Payments.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Payments.AsNoTracking().Where(item => item.ZevId == zevId
                    && item.Status == PaymentStatus.Posted)
                .Select(item => new { item.Id, item.PersonId, item.FundId, item.BankTransactionId,
                    item.PaidOn, item.Amount, allocated = database.PaymentAllocations
                        .Where(allocation => allocation.PaymentId == item.Id)
                        .Sum(allocation => (decimal?)allocation.Amount) ?? 0m })
                .Where(item => item.Amount > item.allocated);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.PaidOn).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount,
                items = items.Select(item => new { item.Id, item.PersonId, item.FundId,
                    item.BankTransactionId, item.PaidOn, item.Amount, item.allocated,
                    unallocated = item.Amount - item.allocated }) });
        });

        group.MapGet("/owner-card/{personId:guid}", async (Guid zevId, Guid personId, DateOnly? from,
            DateOnly? to, int? page, int? pageSize, TenantAccess access, OwnerCardService ownerCard,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "OwnerLedger.View", cancellationToken)) return Results.NotFound();
            if (from > to) return Results.BadRequest(new { error = "From date must not follow to date." });
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            return Results.Ok(await ownerCard.ReadAsync(zevId, personId, from, to,
                currentPage, currentSize, cancellationToken));
        });

        group.MapGet("/unit-ledger/{unitId:guid}", async (Guid zevId, Guid unitId, Guid? personId,
            DateOnly? from, DateOnly? to, int? page, int? pageSize, TenantAccess access,
            ZevDbContext database, OwnerCardService ownerCard,
            CancellationToken cancellationToken) =>
        {
            if (!await access.CanReadZevAsync(zevId, cancellationToken) || from > to)
                return Results.NotFound();
            var canViewAll = await access.HasPermissionAsync(zevId, "OwnerLedger.View", cancellationToken);
            if (!canViewAll
                && (personId is not Guid ownerId
                    || !await access.CanAccessOwnPersonAsync(zevId, ownerId, null,
                        cancellationToken))) return Results.NotFound();
            if (!canViewAll
                && !await database.Ownerships.AnyAsync(item => item.ZevId == zevId && item.PropertyUnitId == unitId
                    && item.PersonId == personId, cancellationToken)) return Results.NotFound();
            if (!await database.PropertyUnits.AnyAsync(item => item.ZevId == zevId && item.Id == unitId,
                cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var card = await ownerCard.ReadUnitAsync(zevId, unitId, personId, from, to,
                currentPage, currentSize, cancellationToken);
            var openingBalances = from is DateOnly start
                ? (await ownerCard.ReadUnitAsync(zevId, unitId, personId, null, start.AddDays(-1),
                    1, 1, cancellationToken, balancesOnly: true)).Balances
                : [];
            var single = card.Balances.Count == 1 ? card.Balances[0] : null;
            var singleOpening = openingBalances.Count == 1 ? openingBalances[0] : null;
            return Results.Ok(new { page = card.Page, pageSize = card.PageSize,
                totalCount = card.TotalCount,
                balancesByCurrency = card.Balances.Select(item => new { item.CurrencyCode,
                    charges = item.TotalCharges, paid = item.TotalPayments, item.Open,
                    balance = item.NetBalance }),
                openingByCurrency = openingBalances.Select(item => new { item.CurrencyCode,
                    balance = item.NetBalance }),
                opening = from == null ? (decimal?)0m : singleOpening?.NetBalance,
                balance = single?.NetBalance,
                items = card.Events.Select(item => new { item.Date, item.Kind, item.PersonId,
                    item.ChargeId, item.PaymentId, item.Description, item.Document,
                    item.CurrencyCode, signedAmount = item.Debt - item.Payment,
                    balance = item.RunningBalance }) });
        });

        return api;
    }
}

public sealed record CreateBankAccountRequest(Guid FundId, string AccountNumber, string CurrencyCode, string? BankName);
public sealed record ImportStatementRequest(Guid BankAccountId, string FileName, string Format, string ContentBase64);
public sealed record AllocationLine(Guid ChargeId, decimal Amount);
public sealed record ManualAllocationRequest(List<AllocationLine> Allocations, Guid? RequestId = null);
public sealed record BankReturnRequest(Guid PaymentId, string Reason);
public sealed record LedgerEntry(DateOnly Date, string Kind, Guid PersonId, Guid ChargeId,
    Guid? PaymentId, decimal SignedAmount, string Description);
public sealed record OwnerPaymentEvent(DateOnly Date, string Kind, string Document, string Description,
    decimal Debt, decimal Payment, decimal CreditDelta, Guid? ChargeId, Guid? PaymentId);
