using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Payments;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class PaymentSlipEndpoints
{
    public static RouteGroupBuilder MapPaymentSlipEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/zevs/{zevId:guid}/charges/{chargeId:guid}/payment-slip.pdf", async (Guid zevId,
            Guid chargeId, TenantAccess access, ZevDbContext database, IPaymentSlipProvider provider,
            PaymentReferenceService references,
            CancellationToken cancellationToken) =>
        {
            if (!await access.CanReadZevAsync(zevId, cancellationToken) || access.CurrentUserId is not Guid userId)
                return Results.NotFound();
            var charge = await database.Charges.AsNoTracking().Where(item => item.Id == chargeId
                && item.ZevId == zevId).Select(item => new { item.Id, item.PersonId, item.PropertyUnitId,
                    payer = item.Person.DisplayName, beneficiary = item.PropertyUnit.Entrance.Building.Zev.Name,
                    item.FundId, item.Reference, item.Amount, item.DueDate }).SingleOrDefaultAsync(cancellationToken);
            if (charge is null || (!await access.HasPermissionAsync(zevId, "Invoice.Export", cancellationToken)
                && (!await access.HasPermissionAsync(zevId, "Finance.View", cancellationToken, staffOnly: false)
                    || !await access.CanAccessOwnPersonAsync(zevId, charge.PersonId, null,
                        cancellationToken)))) return Results.NotFound();
            var account = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId
                && item.FundId == charge.FundId && item.IsActive).OrderBy(item => item.Id)
                .Select(item => new { item.AccountNumber, item.CurrencyCode }).FirstOrDefaultAsync(cancellationToken);
            if (account is null) return Results.Conflict();
            var balances = await FinancialReadModel.ChargeIdsAsync(database, zevId,
                [charge.Id], null, cancellationToken);
            if (balances.Count != 1 || balances[0].CurrencyCode != account.CurrencyCode)
                return Results.Conflict(new { error = "Payment slip requires a verified charge currency." });
            var outstanding = balances[0].Open;
            if (outstanding <= 0m) return Results.Conflict(new { error = "Charge is already settled." });
            var pdf = provider.Generate(new PaymentSlipData(charge.beneficiary, account.AccountNumber,
                charge.payer, await references.EnsureAsync(zevId, charge.PropertyUnitId, charge.PersonId, cancellationToken),
                outstanding, account.CurrencyCode, charge.DueDate));
            return Results.File(pdf, "application/pdf", $"uplatnica-{charge.Id:N}.pdf");
        });
        return api;
    }
}
