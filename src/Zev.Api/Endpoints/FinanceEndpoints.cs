using System.Data;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Payments;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class FinanceEndpoints
{
    public static RouteGroupBuilder MapFinanceEndpoints(this RouteGroupBuilder api)
    {
        var finance = api.MapGroup("/zevs/{zevId:guid}/finance");

        finance.MapGet("/funds", async (Guid zevId, TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Funds.View", cancellationToken)) return Results.NotFound();
            var funds = await database.Funds.AsNoTracking().Where(item => item.ZevId == zevId)
                .OrderBy(item => item.Kind).ToListAsync(cancellationToken);
            var balances = await FinancialReadModel.FundMovements(database, zevId)
                .GroupBy(item => new { item.FundId, item.CurrencyCode })
                .Select(group => new { group.Key.FundId, group.Key.CurrencyCode,
                    balance = group.Sum(item => item.Amount) }).ToListAsync(cancellationToken);
            return Results.Ok(funds.Select(item => new
            {
                item.Id, item.ZevId, kind = item.Kind.ToString(), item.Name, item.IsActive,
                item.RequiresBankAccountForInvoice,
                balance = balances.Where(balance => balance.FundId == item.Id)
                    .Select(balance => balance.CurrencyCode).Distinct().Count() == 1
                    && balances.Any(balance => balance.FundId == item.Id
                        && balance.CurrencyCode != FinancialReadModel.UnknownCurrency)
                    ? (decimal?)balances.Single(balance => balance.FundId == item.Id).balance : null,
                currencyCode = balances.Count(balance => balance.FundId == item.Id) == 1
                    ? balances.First(balance => balance.FundId == item.Id).CurrencyCode : null,
                balancesByCurrency = balances.Where(balance => balance.FundId == item.Id)
                    .Select(balance => new { balance.CurrencyCode, balance.balance }),
                rowVersion = Convert.ToBase64String(item.RowVersion)
            }));
        });

        finance.MapGet("/units", async (Guid zevId, string? search, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "PropertyUnits.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.PropertyUnits.AsNoTracking().Where(item => item.ZevId == zevId && item.IsActive);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Code.Contains(search.Trim()));
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Code).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.Code, item.AreaSquareMeters, item.RegisteredMembers })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        finance.MapPost("/funds", async (Guid zevId, CreateFundRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Funds.Create", cancellationToken)) return Results.NotFound();
            if (!Enum.TryParse<FundKind>(request.Kind, true, out var kind) || !Enum.IsDefined(kind)
                || !AdminValidation.Required(request.Name, 120)) return Results.BadRequest();
            var zev = await database.Zevs.SingleAsync(item => item.Id == zevId, cancellationToken);
            if (await database.Funds.AnyAsync(item => item.ZevId == zevId && item.Kind == kind, cancellationToken)) return Results.Conflict();
            var fund = new Fund { OrganizationId = zev.OrganizationId, ZevId = zevId, Kind = kind,
                Name = request.Name.Trim(), RequiresBankAccountForInvoice = request.RequiresBankAccountForInvoice };
            database.Funds.Add(fund);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Created($"/api/zevs/{zevId}/finance/funds", new { fund.Id, kind = fund.Kind.ToString(),
                    fund.Name, fund.RequiresBankAccountForInvoice });
            }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        finance.MapPut("/funds/{fundId:guid}/bank-account-requirement", async (Guid zevId, Guid fundId,
            FundBankAccountRequirementRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Funds.Edit", cancellationToken)) return Results.NotFound();
            var fund = await database.Funds.SingleOrDefaultAsync(item => item.ZevId == zevId
                && item.Id == fundId, cancellationToken);
            if (fund is null) return Results.NotFound();
            fund.RequiresBankAccountForInvoice = request.RequiresBankAccountForInvoice;
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
            return Results.NoContent();
        });

        finance.MapGet("/tariffs", async (Guid zevId, int? page, int? pageSize, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Finance.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.TariffVersions.AsNoTracking().Where(item => item.ZevId == zevId);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Code).ThenByDescending(item => item.Version)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).Select(item => new
                {
                    item.Id, item.Code, item.Version, item.Name, basis = item.Basis.ToString(), item.Rate,
                    item.Description, item.FundId, fundName = item.Fund.Name,
                    item.ValidFrom, item.ValidTo, item.IsActive,
                    item.IsOptional, item.ShowOnInvoice, item.DisplayOrder,
                    rowVersion = Convert.ToBase64String(item.RowVersion)
                }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        finance.MapGet("/billing-items", async (Guid zevId, DateOnly? period, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Finance.View", cancellationToken)) return Results.NotFound();
            if (period is not DateOnly selectedPeriod || selectedPeriod.Day != 1) return Results.BadRequest();
            var items = await database.TariffVersions.AsNoTracking().Where(item => item.ZevId == zevId
                && item.IsActive && item.Basis != TariffBasis.Extraordinary
                && item.ValidFrom <= selectedPeriod && (item.ValidTo == null || item.ValidTo > selectedPeriod)
                && (item.Basis != TariffBasis.OneTime || item.ValidFrom == selectedPeriod))
                .OrderBy(item => item.Code).ThenByDescending(item => item.Version)
                .Select(item => new
                {
                    item.Id, item.Code, item.Version, item.Name, basis = item.Basis.ToString(), item.Rate,
                    item.FundId, fundName = item.Fund.Name, fundIsActive = item.Fund.IsActive,
                    item.ValidFrom, item.ValidTo, item.IsActive, item.IsOptional,
                    item.ShowOnInvoice, item.DisplayOrder
                }).ToListAsync(cancellationToken);
            return Results.Ok(items);
        });

        finance.MapPost("/tariffs", async (Guid zevId, CreateTariffRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Finance.Create", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.Code(request.Code) || !AdminValidation.Required(request.Name, 200)
                || !TryBasis(request.Basis, out var basis)
                || request.Rate < 0m || request.Rate > 99999999999999m
                || !AdminValidation.Optional(request.Description, 1000) || request.DisplayOrder < 0
                || request.ValidFrom.Day != 1 || request.ValidTo is DateOnly end && end <= request.ValidFrom)
                return Results.BadRequest();
            var fund = await database.Funds.SingleOrDefaultAsync(item => item.Id == request.FundId
                && item.ZevId == zevId && item.IsActive, cancellationToken);
            if (fund is null) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var versions = await database.TariffVersions.Where(item => item.ZevId == zevId && item.Code == code)
                .OrderByDescending(item => item.Version).ToListAsync(cancellationToken);
            var latest = versions.FirstOrDefault();
            if (latest is not null && (request.ValidFrom <= latest.ValidFrom
                || latest.ValidTo is DateOnly previousEnd && request.ValidFrom < previousEnd)) return Results.Conflict();
            if (latest is not null && latest.ValidTo is null) latest.ValidTo = request.ValidFrom;
            var tariff = new TariffVersion
            {
                OrganizationId = fund.OrganizationId, ZevId = zevId, FundId = fund.Id, Code = code,
                Version = (latest?.Version ?? 0) + 1, Name = request.Name.Trim(), Basis = basis,
                Rate = request.Rate, ValidFrom = request.ValidFrom, ValidTo = request.ValidTo,
                Description = AdminValidation.Clean(request.Description), IsActive = request.IsActive,
                IsOptional = request.IsOptional || basis is TariffBasis.ManualAmount or TariffBasis.OneTime,
                ShowOnInvoice = request.ShowOnInvoice, DisplayOrder = request.DisplayOrder
            };
            database.TariffVersions.Add(tariff);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Results.Created($"/api/zevs/{zevId}/finance/tariffs", new { tariff.Id, tariff.Code, tariff.Version });
            }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        finance.MapPatch("/tariffs/{tariffId:guid}/status", async (Guid zevId, Guid tariffId,
            UpdateTariffStatusRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Finance.Edit", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var tariff = await database.TariffVersions.SingleOrDefaultAsync(item => item.Id == tariffId
                && item.ZevId == zevId, cancellationToken);
            if (tariff is null) return Results.NotFound();
            database.Entry(tariff).Property(item => item.RowVersion).OriginalValue = rowVersion;
            tariff.IsActive = request.IsActive;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { tariff.Id, tariff.IsActive,
                    rowVersion = Convert.ToBase64String(tariff.RowVersion) });
            }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
        });

        finance.MapPost("/billing-runs/preflight", async (Guid zevId, GenerateBillingRequest request, TenantAccess access,
            MonthlyBillingEngine engine, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Billing.Preview", cancellationToken)) return Results.NotFound();
            return Results.Ok(await engine.PreflightAsync(zevId, request.Period, request.DueDate, request.Draft,
                cancellationToken));
        });

        finance.MapPost("/billing-runs/preview", async (Guid zevId, GenerateBillingRequest request, TenantAccess access,
            MonthlyBillingEngine engine, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Billing.Preview", cancellationToken)) return Results.NotFound();
            var result = await engine.PreviewAsync(zevId, request.Period, request.DueDate, request.Draft,
                cancellationToken);
            return result.Failure == BillingFailure.None ? Results.Ok(result)
                : result.Failure == BillingFailure.AlreadyPosted ? Results.Conflict(result)
                : result.Failure == BillingFailure.Missing ? Results.NotFound(result) : Results.BadRequest(result);
        });

        finance.MapPost("/billing-runs", async (Guid zevId, GenerateBillingRequest request, TenantAccess access,
            MonthlyBillingEngine engine, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Billing.Post", cancellationToken)) return Results.NotFound();
            var result = await engine.GenerateAsync(zevId, request.Period, request.DueDate, request.Draft,
                cancellationToken);
            if (result.Run is null) return result.Failure == BillingFailure.Invalid
                ? Results.BadRequest(new { error = result.Detail ?? "Billing request is invalid." })
                : Results.Conflict(new { error = result.Detail ?? "Billing conflict; refresh preview and retry." });
            return Results.Ok(new { result.Run.Id, result.Run.Period, result.Run.DueDate,
                result.Run.ChargeCount, result.Run.TotalAmount, result.AlreadyExists });
        });

        finance.MapGet("/charges", async (Guid zevId, DateOnly? period, Guid? personId, string? reference, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Billing.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Charges.AsNoTracking().Where(item => item.ZevId == zevId);
            if (period is DateOnly selectedPeriod) query = query.Where(item => item.Period == selectedPeriod);
            if (personId is Guid selectedPerson) query = query.Where(item => item.PersonId == selectedPerson);
            if (!string.IsNullOrWhiteSpace(reference)) query = query.Where(item => item.Reference == reference.Trim());
            var totalCount = await query.CountAsync(cancellationToken);
            var facts = FinancialReadModel.Charges(database, zevId, null);
            var items = await (from item in query
                join fact in facts on item.Id equals fact.Id
                select new { item, fact.CurrencyCode })
                .OrderByDescending(row => row.item.Period).ThenBy(row => row.item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(row => new { row.item.Id, row.item.PropertyUnitId, row.item.PersonId,
                    personName = row.item.Person.DisplayName, row.item.FundId,
                    row.item.TariffVersionId, row.item.Period, row.item.DueDate,
                    row.item.BasisQuantity, row.item.RateSnapshot, row.item.OwnershipShareSnapshot,
                    row.item.Amount, row.item.Reference, row.CurrencyCode })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        finance.MapPost("/extraordinary-charges", async (Guid zevId, ExtraordinaryChargeRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Finance.Create", cancellationToken)) return Results.NotFound();
            if (request.Amount <= 0m || request.Period.Day != 1 || request.DueDate < request.Period) return Results.BadRequest();
            var tariff = await database.TariffVersions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.TariffVersionId
                && item.ZevId == zevId && item.Basis == TariffBasis.Extraordinary && item.IsActive
                && item.ValidFrom <= request.Period && (item.ValidTo == null || item.ValidTo > request.Period), cancellationToken);
            var unit = await database.PropertyUnits.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.PropertyUnitId
                && item.ZevId == zevId && item.IsActive, cancellationToken);
            if (tariff is null || unit is null || !await database.Ownerships.AnyAsync(item => item.PropertyUnitId == unit.Id
                && item.PersonId == request.PersonId && item.ValidFrom <= request.Period
                && (item.ValidTo == null || item.ValidTo > request.Period), cancellationToken)) return Results.NotFound();
            var charge = new Charge
            {
                OrganizationId = unit.OrganizationId, ZevId = zevId, PropertyUnitId = unit.Id,
                PersonId = request.PersonId, FundId = tariff.FundId, TariffVersionId = tariff.Id,
                Period = request.Period, DueDate = request.DueDate, BasisQuantity = 1m, RateSnapshot = request.Amount,
                OwnershipShareSnapshot = 1m, Amount = request.Amount
            };
            charge.Reference = $"ZEV-{charge.Id:N}";
            database.Charges.Add(charge);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/finance/charges", new { charge.Id, charge.Reference, charge.Amount });
        });

        return api;
    }

    private static bool TryBasis(string? value, out TariffBasis basis)
    {
        var normalized = value?.Trim().ToUpperInvariant() switch
        {
            "PER_M2" => nameof(TariffBasis.SquareMeter),
            "FIXED" => nameof(TariffBasis.FixedUnit),
            "PER_MEMBER" => nameof(TariffBasis.RegisteredMember),
            "MANUAL_AMOUNT" => nameof(TariffBasis.ManualAmount),
            "ONE_TIME" => nameof(TariffBasis.OneTime),
            _ => value
        };
        return Enum.TryParse(normalized, true, out basis) && Enum.IsDefined(basis);
    }
}

public sealed record CreateFundRequest(string Kind, string Name, bool RequiresBankAccountForInvoice = false);
public sealed record FundBankAccountRequirementRequest(bool RequiresBankAccountForInvoice);
public sealed record CreateTariffRequest(string Code, string Name, string Basis, decimal Rate, Guid FundId,
    DateOnly ValidFrom, DateOnly? ValidTo = null, string? Description = null, bool IsActive = true,
    bool IsOptional = false, bool ShowOnInvoice = true, int DisplayOrder = 0);
public sealed record GenerateBillingRequest(DateOnly Period, DateOnly DueDate, BillingDraftRequest? Draft = null);
public sealed record UpdateTariffStatusRequest(bool IsActive, string RowVersion);
public sealed record ExtraordinaryChargeRequest(Guid TariffVersionId, Guid PropertyUnitId, Guid PersonId,
    DateOnly Period, DateOnly DueDate, decimal Amount);
