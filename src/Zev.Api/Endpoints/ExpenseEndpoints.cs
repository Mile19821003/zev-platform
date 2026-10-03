using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Payments;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class ExpenseEndpoints
{
    public static RouteGroupBuilder MapExpenseEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/expenses");

        group.MapGet("/suppliers", async (Guid zevId, int? page, int? pageSize, string? search,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Expenses.View", cancellationToken)) return Results.NotFound();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Suppliers.AsNoTracking().Where(item => item.OrganizationId == organizationId);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Name.Contains(search.Trim()));
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name).Skip((currentPage - 1) * currentSize)
                .Take(currentSize).Select(item => new { item.Id, item.Name, item.TaxIdentifier, item.Email,
                    item.Phone, item.IsActive }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("/suppliers", async (Guid zevId, CreateSupplierRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Expenses.Create", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.Required(request.Name, 200) || !AdminValidation.Optional(request.TaxIdentifier, 32)
                || !AdminValidation.Optional(request.Email, 320) || !AdminValidation.Optional(request.Phone, 40))
                return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var supplier = new Supplier { OrganizationId = organizationId, Name = request.Name.Trim(),
                TaxIdentifier = AdminValidation.Clean(request.TaxIdentifier), Email = AdminValidation.Clean(request.Email),
                Phone = AdminValidation.Clean(request.Phone) };
            database.Suppliers.Add(supplier);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/expenses/suppliers", new { supplier.Id, supplier.Name });
        });

        group.MapGet("", async (Guid zevId, int? page, int? pageSize, Guid? fundId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Expenses.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Expenses.AsNoTracking().Where(item => item.ZevId == zevId);
            if (fundId is Guid selectedFund) query = query.Where(item => item.FundId == selectedFund);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.InvoiceDate).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).Select(item => new
                {
                    item.Id, item.FundId, item.SupplierId, supplierName = item.Supplier.Name,
                    item.InvoiceNumber, item.InvoiceDate, item.Amount, item.Category, item.Description,
                    currencyCode = FinancialReadModel.UnknownCurrency,
                    item.DocumentId, status = item.Status.ToString(), rowVersion = Convert.ToBase64String(item.RowVersion)
                }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("", async (Guid zevId, CreateExpenseRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Expenses.Create", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.Required(request.InvoiceNumber, 100) || !AdminValidation.Required(request.Category, 100)
                || !AdminValidation.Optional(request.Description, 1000) || request.Amount <= 0m
                || request.InvoiceDate == default) return Results.BadRequest();
            var fund = await database.Funds.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.FundId
                && item.ZevId == zevId && item.IsActive, cancellationToken);
            if (fund is null || !await database.Suppliers.AnyAsync(item => item.Id == request.SupplierId
                    && item.OrganizationId == fund.OrganizationId && item.IsActive, cancellationToken)
                || (request.DocumentId is Guid documentId && !await database.StoredFiles.AnyAsync(item => item.Id == documentId
                    && item.ZevId == zevId, cancellationToken))) return Results.NotFound();
            var expense = new Expense
            {
                OrganizationId = fund.OrganizationId, ZevId = zevId, FundId = fund.Id,
                SupplierId = request.SupplierId, DocumentId = request.DocumentId,
                InvoiceNumber = request.InvoiceNumber.Trim(), InvoiceDate = request.InvoiceDate,
                Amount = request.Amount, Category = request.Category.Trim(),
                Description = AdminValidation.Clean(request.Description)
            };
            database.Expenses.Add(expense);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Created($"/api/zevs/{zevId}/expenses/{expense.Id}", new { expense.Id });
            }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapPost("/{expenseId:guid}/post", async (Guid zevId, Guid expenseId, TenantAccess access,
            ExpensePostingService service, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Expenses.Post", cancellationToken)) return Results.NotFound();
            return await service.PostAsync(zevId, expenseId, cancellationToken)
                ? Results.Ok(new { status = "Posted" }) : Results.Conflict();
        });

        group.MapPost("/{expenseId:guid}/reverse", async (Guid zevId, Guid expenseId, TenantAccess access,
            ExpensePostingService service, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Expenses.Reverse", cancellationToken)) return Results.NotFound();
            return await service.ReverseAsync(zevId, expenseId, cancellationToken)
                ? Results.Ok(new { status = "Reversed" }) : Results.Conflict();
        });

        group.MapGet("/debtors", async (Guid zevId, DateOnly? asOf, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Expenses.View", cancellationToken)) return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var open = FinancialReadModel.OpenItems(database, zevId, date)
                .Where(item => item.DueDate < date
                    && item.Amount + item.Correction > item.Allocated);
            var grouped = open.GroupBy(item => new { item.PersonId, item.PersonName,
                item.CurrencyCode }).Select(group => new
            {
                group.Key.PersonId, personName = group.Key.PersonName, group.Key.CurrencyCode,
                outstanding = group.Sum(item => item.Amount + item.Correction - item.Allocated),
                oldestDue = group.Min(item => item.DueDate),
                openItems = group.Count()
            });
            var totalCount = await grouped.CountAsync(cancellationToken);
            var items = await grouped.OrderByDescending(item => item.outstanding).ThenBy(item => item.PersonId)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        return api;
    }
}

public sealed record CreateSupplierRequest(string Name, string? TaxIdentifier, string? Email, string? Phone);
public sealed record CreateExpenseRequest(Guid FundId, Guid SupplierId, Guid? DocumentId,
    string InvoiceNumber, DateOnly InvoiceDate, decimal Amount, string Category, string? Description);
