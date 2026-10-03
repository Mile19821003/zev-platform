using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Payments;

namespace Zev.Api.Finance;

public enum BillingFailure { None, Invalid, Missing, Conflict, AlreadyPosted }
public sealed record BillingResult(BillingRun? Run, BillingFailure Failure, bool AlreadyExists = false,
    string? Detail = null);
public sealed record BillingDraftAdjustment(Guid PropertyUnitId, Guid TariffVersionId, bool? Include,
    decimal? ManualAmount, string? Note, string? Reason, string? Description = null,
    string? UnitOfMeasure = null, decimal? Quantity = null, decimal? UnitPrice = null,
    string? DiscountType = null, decimal? DiscountValue = null);
public sealed record BillingDraftRequest(IReadOnlyList<Guid>? TariffVersionIds,
    IReadOnlyList<BillingDraftAdjustment>? Adjustments, string? ExpectedHash,
    IReadOnlyList<BillingManualLine>? ManualLines = null, string? ExpectedCreditPlanHash = null);
public sealed record BillingManualLine(Guid Id, Guid PropertyUnitId, Guid FundId, string Description,
    string UnitOfMeasure, decimal Quantity, decimal UnitPrice, string? DiscountType,
    decimal? DiscountValue, string? Note, string Reason);
public sealed record BillingPreviewLine(Guid? TariffVersionId, string Code, string Name, string Basis,
    Guid FundId, string FundName, decimal Quantity, decimal Rate, decimal OwnershipShare, decimal Amount,
    int DisplayOrder, bool ShowOnInvoice,
    bool IsManualOverride, string? Note, decimal GrossAmount = 0m, string DiscountType = "None",
    decimal DiscountValue = 0m, decimal DiscountAmount = 0m, string? UnitOfMeasure = null);
public sealed record BillingPreviewUnit(Guid PropertyUnitId, string UnitCode, Guid PersonId, string PersonName,
    decimal AreaSquareMeters, int RegisteredMembers, IReadOnlyList<BillingPreviewLine> Lines, decimal Total)
{
    public decimal AvailableCredit { get; init; }
    public decimal ProposedCredit { get; init; }
    public decimal RemainingToPay => Total - ProposedCredit;
}
public sealed record BillingPreviewResult(BillingFailure Failure, string? Fingerprint,
    IReadOnlyList<BillingPreviewUnit> Units, decimal TotalAmount, int ChargeCount, Guid? ExistingRunId = null)
{
    public string? Detail { get; init; }
    public IReadOnlyList<BillingPreflightIssue> Issues { get; init; } = [];
    public bool AutoCreditEnabled { get; init; }
    public decimal ProposedCreditTotal { get; init; }
    public string? CreditPlanHash { get; init; }
    public IReadOnlyList<BillingCreditAllocation> CreditAllocations { get; init; } = [];
    public string Currency { get; init; } = "BAM";
    public decimal RemainingToPay => TotalAmount - ProposedCreditTotal;
}
public sealed record BillingCreditAllocation(Guid PaymentId, Guid ChargeId, Guid PropertyUnitId,
    bool ExistingCharge, decimal Amount);
public sealed record BillingPreflightIssue(string Code, string Message, BillingFailure Failure,
    string? PropertyUnitCode = null, string? TariffCode = null, string? FundName = null);
public sealed record BillingPreflightResult(IReadOnlyList<BillingPreflightIssue> Issues, Guid? ExistingRunId = null)
{
    public bool CanPreview => Issues.Count == 0;
    public BillingFailure Failure => Issues.FirstOrDefault()?.Failure ?? BillingFailure.None;
}

public sealed class MonthlyBillingEngine(ZevDbContext database, IPaymentReferenceProvider? referenceProvider = null,
    IConfiguration? configuration = null, IHttpContextAccessor? httpContextAccessor = null,
    IInvoiceNumberProvider? invoiceNumberProvider = null)
{
    private readonly IPaymentReferenceProvider references = referenceProvider ?? new PaymentReferenceProvider();
    private readonly IInvoiceNumberProvider numbers = invoiceNumberProvider ?? new InvoiceNumberProvider(configuration);
    public Task<BillingResult> GenerateAsync(Guid zevId, DateOnly period, DateOnly dueDate,
        CancellationToken cancellationToken) => GenerateAsync(zevId, period, dueDate, null, cancellationToken);

    public Task<BillingPreviewResult> PreviewAsync(Guid zevId, DateOnly period, DateOnly dueDate,
        BillingDraftRequest? draft, CancellationToken cancellationToken) =>
        PreviewCoreAsync(zevId, period, dueDate, draft, null, cancellationToken);

    public Task<BillingPreflightResult> PreflightAsync(Guid zevId, DateOnly period, DateOnly dueDate,
        BillingDraftRequest? draft, CancellationToken cancellationToken) =>
        PreflightCoreAsync(zevId, period, dueDate, draft, null, cancellationToken);

    public Task<BillingPreviewResult> PreviewSelectedUnitsAsync(Guid zevId, DateOnly period, DateOnly dueDate,
        BillingDraftRequest draft, IReadOnlyCollection<Guid> propertyUnitIds, CancellationToken cancellationToken) =>
        PreviewCoreAsync(zevId, period, dueDate, draft, propertyUnitIds, cancellationToken);

    private async Task<BillingPreviewResult> PreviewCoreAsync(Guid zevId, DateOnly period, DateOnly dueDate,
        BillingDraftRequest? draft, IReadOnlyCollection<Guid>? propertyUnitIds, CancellationToken cancellationToken)
    {
        var preflight = await PreflightCoreAsync(zevId, period, dueDate, draft, propertyUnitIds, cancellationToken);
        if (!preflight.CanPreview) return Empty(preflight.Failure, preflight.ExistingRunId,
            preflight.Issues[0].Message, preflight.Issues);
        var plan = await BuildPlanAsync(zevId, period, dueDate, draft, propertyUnitIds, cancellationToken);
        if (plan.Items is null) return Empty(plan.Failure, detail: plan.Detail);
        var units = plan.Items.GroupBy(item => new { UnitId = item.Unit.Id, UnitCode = item.Unit.Code,
                item.Unit.AreaSquareMeters, item.Unit.RegisteredMembers, PersonId = item.Person.Id,
                PersonName = item.Person.DisplayName })
            .OrderBy(group => group.Key.UnitCode).ThenBy(group => group.Key.PersonName)
            .Select(group => new BillingPreviewUnit(group.Key.UnitId, group.Key.UnitCode, group.Key.PersonId,
                group.Key.PersonName, group.Key.AreaSquareMeters, group.Key.RegisteredMembers,
                group.OrderBy(item => item.Line.SortOrder).ThenBy(item => item.Line.CodeSnapshot)
                    .Select(item => new BillingPreviewLine(item.Tariff?.Id, item.Line.CodeSnapshot, item.Line.DescriptionSnapshot,
                        item.Tariff?.Basis.ToString() ?? TariffBasis.ManualAmount.ToString(), item.Line.FundId, item.Fund.Name,
                        item.Charge.BasisQuantity,
                        item.Charge.RateSnapshot, item.Charge.OwnershipShareSnapshot, item.Charge.Amount,
                        item.Line.SortOrder,
                        item.Line.ShowOnInvoice, item.Charge.IsManualOverride, item.Charge.Note,
                        item.Line.AmountBeforeDiscount, item.Line.DiscountType.ToString(), item.Line.DiscountValue,
                        item.Line.DiscountAmount, item.Line.UnitOfMeasureSnapshot)).ToList(),
                group.Sum(item => item.Charge.Amount))).ToList();
        var autoCreditEnabled = await database.Zevs.AsNoTracking().Where(item => item.Id == zevId)
            .Select(item => item.AutoApplyPaymentCredit).SingleAsync(cancellationToken);
        decimal proposedCreditTotal = 0m;
        var billedFundIds = plan.Items.Select(item => item.Fund.Id).Distinct().ToArray();
        var currencies = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId
                && item.IsActive && billedFundIds.Contains(item.FundId))
            .Select(item => item.CurrencyCode).Distinct().ToListAsync(cancellationToken);
        if (currencies.Count > 1)
            return Empty(BillingFailure.Conflict, detail: "Selected billing funds use multiple currencies.");
        var currency = currencies.SingleOrDefault()
            ?? configuration?["Finance:DefaultCurrency"]?.Trim().ToUpperInvariant() ?? "BAM";
        BillingCreditPlan? creditPlan = null;
        if (autoCreditEnabled)
        {
            creditPlan = await PlanCreditAsync(zevId, plan, currency, cancellationToken);
            units = units.Select(unit =>
            {
                var applied = creditPlan.Allocations.Where(item => !item.ExistingCharge
                    && item.PropertyUnitId == unit.PropertyUnitId).Sum(item => item.Amount);
                proposedCreditTotal += applied;
                return unit with { AvailableCredit = applied, ProposedCredit = applied };
            }).ToList();
        }
        return new(BillingFailure.None, plan.Fingerprint, units,
            plan.Items.Sum(item => item.Charge.Amount), plan.Items.Count)
        {
            AutoCreditEnabled = autoCreditEnabled, ProposedCreditTotal = proposedCreditTotal,
            CreditPlanHash = creditPlan?.Hash, CreditAllocations = creditPlan?.Allocations ?? [],
            Currency = currency
        };
    }

    private async Task<BillingPreflightResult> PreflightCoreAsync(Guid zevId, DateOnly period, DateOnly dueDate,
        BillingDraftRequest? draft, IReadOnlyCollection<Guid>? propertyUnitIds, CancellationToken cancellationToken)
    {
        var issues = new List<BillingPreflightIssue>();
        if (period.Day != 1 || dueDate < period)
        {
            issues.Add(new("INVALID_PERIOD", "Period must start on the first day of a month and due date must not precede it.",
                BillingFailure.Invalid));
            return new(issues);
        }

        var zevExists = await database.Zevs.AsNoTracking().AnyAsync(item => item.Id == zevId
            && item.IsActive && item.Organization.IsActive, cancellationToken);
        if (!zevExists)
        {
            issues.Add(new("ZEV_NOT_ACTIVE", "Selected ZEV is not active or was not found.", BillingFailure.Missing));
            return new(issues);
        }

        var existingRun = await database.BillingRuns.AsNoTracking().Where(item => item.ZevId == zevId
            && item.Period == period).Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);
        if (existingRun is Guid runId)
            issues.Add(new("BILLING_ALREADY_POSTED", "Billing for the selected period has already been posted.",
                BillingFailure.AlreadyPosted));

        var units = await database.PropertyUnits.AsNoTracking().Where(item => item.ZevId == zevId && item.IsActive
            && item.Entrance.IsActive && item.Entrance.Building.IsActive)
            .Select(item => new { item.Id, item.Code }).ToListAsync(cancellationToken);
        if (propertyUnitIds is not null)
        {
            var selectedUnitIds = propertyUnitIds.ToHashSet();
            if (selectedUnitIds.Count == 0 || selectedUnitIds.Count != propertyUnitIds.Count
                || units.Count(item => selectedUnitIds.Contains(item.Id)) != selectedUnitIds.Count)
            {
                issues.Add(new("SELECTED_UNIT_INVALID", "Selected property unit is not active in selected ZEV.",
                    BillingFailure.Invalid));
                return new(issues, existingRun);
            }
            units = units.Where(item => selectedUnitIds.Contains(item.Id)).ToList();
        }
        if (units.Count == 0)
            issues.Add(new("NO_ACTIVE_UNITS", "No active property units found.", BillingFailure.Conflict));
        else
        {
            var unitIds = units.Select(item => item.Id).ToArray();
            var ownedUnitIds = await database.Ownerships.AsNoTracking().Where(item => unitIds.Contains(item.PropertyUnitId)
                && item.ValidFrom <= period && (item.ValidTo == null || item.ValidTo > period))
                .Select(item => item.PropertyUnitId).Distinct().ToListAsync(cancellationToken);
            var owned = ownedUnitIds.ToHashSet();
            foreach (var unit in units.Where(item => !owned.Contains(item.Id)).OrderBy(item => item.Code))
                issues.Add(new("PROPERTY_UNIT_NO_ACTIVE_OWNER",
                    $"Property unit {unit.Code} has no active owner for selected period.",
                    BillingFailure.Conflict, unit.Code));
        }

        var versions = await database.TariffVersions.AsNoTracking().Include(item => item.Fund)
            .Where(item => item.ZevId == zevId && item.Basis != TariffBasis.Extraordinary)
            .ToListAsync(cancellationToken);
        var validVersions = versions.Where(item => item.IsActive && item.ValidFrom <= period
            && (item.ValidTo is null || item.ValidTo > period)
            && (item.Basis != TariffBasis.OneTime || item.ValidFrom == period)).ToList();
        var selectedIds = draft?.TariffVersionIds is null
            ? validVersions.Where(item => !item.IsOptional && item.Basis != TariffBasis.ManualAmount)
                .Select(item => item.Id).ToHashSet()
            : draft.TariffVersionIds.ToHashSet();
        var referencedIds = selectedIds.Concat(draft?.Adjustments?.Select(item => item.TariffVersionId) ?? [])
            .ToHashSet();
        var selectedCodes = versions.Where(item => referencedIds.Contains(item.Id))
            .Select(item => item.Code).ToHashSet();
        var relevantVersions = draft?.TariffVersionIds is null
            ? validVersions
            : validVersions.Where(item => selectedCodes.Contains(item.Code)).ToList();
        foreach (var group in relevantVersions.GroupBy(item => item.Code).Where(group => group.Count() > 1)
            .OrderBy(group => group.Key))
            issues.Add(new("MULTIPLE_ACTIVE_TARIFF_VERSIONS",
                $"Multiple active tariff versions exist for code {group.Key}.",
                BillingFailure.Conflict, TariffCode: group.Key));
        foreach (var selectedId in selectedIds.OrderBy(item => item))
        {
            var version = versions.SingleOrDefault(item => item.Id == selectedId);
            if (version is null || !validVersions.Any(item => item.Id == selectedId))
                issues.Add(new("SELECTED_TARIFF_INVALID_PERIOD", "Selected tariff is not valid for selected period.",
                    BillingFailure.Invalid, TariffCode: version?.Code));
            else if (!version.Fund.IsActive)
                issues.Add(new("TARIFF_FUND_INACTIVE",
                    $"Tariff {version.Code} has no active fund for selected period.",
                    BillingFailure.Conflict, TariffCode: version.Code));
        }
        if (selectedIds.Count == 0 && (draft?.Adjustments?.Count ?? 0) == 0
            && (draft?.ManualLines?.Count ?? 0) == 0)
            issues.Add(new("NO_ITEMS_SELECTED", "No billing items are selected for the period.", BillingFailure.Invalid));
        if (issues.Count == 0)
        {
            var plan = await BuildPlanAsync(zevId, period, dueDate, draft, propertyUnitIds, cancellationToken);
            if (plan.Items is null)
                issues.Add(new("BILLING_DRAFT_INVALID", plan.Detail ?? "Billing draft is invalid.", plan.Failure));
            else
            {
                issues.AddRange(await BankAccountIssuesAsync(zevId, plan.Items, cancellationToken));
                var fundIds = plan.Items.Select(item => item.Fund.Id).Distinct().ToArray();
                var currencies = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId
                        && item.IsActive && fundIds.Contains(item.FundId))
                    .Select(item => item.CurrencyCode).Distinct().ToListAsync(cancellationToken);
                if (currencies.Count > 1)
                    issues.Add(new("BILLING_MULTIPLE_CURRENCIES",
                        "Selected billing funds use multiple currencies.", BillingFailure.Conflict));
            }
        }
        return new(issues, existingRun);
    }

    public async Task<BillingResult> GenerateAsync(Guid zevId, DateOnly period, DateOnly dueDate,
        BillingDraftRequest? draft, CancellationToken cancellationToken)
    {
        if (period.Day != 1 || dueDate < period) return new(null, BillingFailure.Invalid);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [Zevs] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {zevId}", cancellationToken);
        var previous = await database.BillingRuns.AsNoTracking().SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Period == period, cancellationToken);
        if (previous is not null) return new(previous, BillingFailure.None, true);
        var plan = await BuildPlanAsync(zevId, period, dueDate, draft, null, cancellationToken);
        if (plan.Items is null) return new(null, plan.Failure);
        if ((await BankAccountIssuesAsync(zevId, plan.Items, cancellationToken)).Count != 0)
            return new(null, BillingFailure.Conflict);
        if (draft?.ExpectedHash is not null && !string.Equals(draft.ExpectedHash, plan.Fingerprint,
            StringComparison.OrdinalIgnoreCase)) return new(null, BillingFailure.Conflict);
        var zev = await database.Zevs.AsNoTracking().SingleAsync(item => item.Id == zevId, cancellationToken);
        var bankAccounts = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId && item.IsActive)
            .OrderBy(item => item.AccountNumber).ToListAsync(cancellationToken);
        var billedFunds = plan.Items.Select(item => item.Line.FundId).ToHashSet();
        var currencies = bankAccounts.Where(item => billedFunds.Contains(item.FundId))
            .Select(item => item.CurrencyCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (currencies.Count > 1) return new(null, BillingFailure.Conflict, Detail:
            "Selected billing funds use multiple currencies.");
        var currency = currencies.SingleOrDefault() ?? configuration?["Finance:DefaultCurrency"]?.Trim().ToUpperInvariant() ?? "BAM";
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new InvalidOperationException("Finance:DefaultCurrency must be a three-letter ISO currency code.");
        var creditPlan = zev.AutoApplyPaymentCredit
            ? await PlanCreditAsync(zevId, plan, currency, cancellationToken) : null;
        if (draft?.ExpectedCreditPlanHash is not null
            && !string.Equals(draft.ExpectedCreditPlanHash, creditPlan?.Hash, StringComparison.OrdinalIgnoreCase))
            return new(null, BillingFailure.Conflict, Detail:
                "Credit availability changed after preview. Refresh the preview before posting.");
        var run = new BillingRun { OrganizationId = plan.OrganizationId, ZevId = zevId, Period = period,
            DueDate = dueDate, ChargeCount = plan.Items.Count, TotalAmount = plan.Items.Sum(item => item.Charge.Amount) };
        var previousCharges = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId && item.Period < period)
            .GroupBy(item => new { item.PropertyUnitId, item.PersonId })
            .Select(group => new { group.Key.PropertyUnitId, group.Key.PersonId, Amount = group.Sum(item => item.Amount) })
            .ToListAsync(cancellationToken);
        var previousPayments = await database.PaymentAllocations.AsNoTracking()
            .Where(item => item.ZevId == zevId && item.Charge.Period < period
                && item.Payment.Status == PaymentStatus.Posted && item.Payment.PaidOn <= DateOnly.FromDateTime(DateTime.UtcNow))
            .GroupBy(item => new { item.Charge.PropertyUnitId, item.Charge.PersonId })
            .Select(group => new { group.Key.PropertyUnitId, group.Key.PersonId, Amount = group.Sum(item => item.Amount) })
            .ToListAsync(cancellationToken);
        var chargeBalances = previousCharges.ToDictionary(item => (item.PropertyUnitId, item.PersonId), item => item.Amount);
        var paidBalances = previousPayments.ToDictionary(item => (item.PropertyUnitId, item.PersonId), item => item.Amount);
        var actor = Guid.TryParse(httpContextAccessor?.HttpContext?.User.FindFirstValue("sub"), out var actorId)
            ? actorId : (Guid?)null;
        var now = DateTimeOffset.UtcNow;
        var issueDate = DateOnly.FromDateTime(now.UtcDateTime);
        var nextSequence = await database.Invoices.AsNoTracking().Where(item => item.ZevId == zevId
            && item.NumberYear == period.Year).Select(item => (int?)item.NumberSequence).MaxAsync(cancellationToken) ?? 0;
        var invoices = new List<Invoice>();
        foreach (var group in plan.Items.GroupBy(item => new { UnitId = item.Unit.Id, PersonId = item.Person.Id })
            .OrderBy(group => group.First().Unit.Code).ThenBy(group => group.First().Person.DisplayName)
            .ThenBy(group => group.Key.UnitId).ThenBy(group => group.Key.PersonId))
        {
            var first = group.First();
            var accountKey = (first.Unit.Id, first.Person.Id);
            var prior = chargeBalances.GetValueOrDefault(accountKey) - paidBalances.GetValueOrDefault(accountKey);
            var invoice = new Invoice
            {
                OrganizationId = plan.OrganizationId, ZevId = zevId, BillingRunId = run.Id,
                PropertyUnitId = first.Unit.Id, PersonId = first.Person.Id,
                NumberYear = period.Year, NumberSequence = ++nextSequence,
                InvoiceNumber = numbers.Format(period.Year, nextSequence),
                Period = period, IssueDate = issueDate, DueDate = dueDate,
                PreviousBalanceSnapshot = prior, CurrentChargeTotal = group.Sum(item => item.Charge.Amount),
                DiscountTotal = group.Sum(item => item.Line.DiscountAmount), TotalAmount = group.Sum(item => item.Charge.Amount),
                TotalDueAtIssue = prior + group.Sum(item => item.Charge.Amount),
                PaymentReferenceSnapshot = references.ForAccount(zevId, first.Unit.Id, first.Person.Id),
                ZevNameSnapshot = zev.Name, ZevAddressSnapshot = zev.AddressLine,
                ZevTaxIdentifierSnapshot = zev.TaxIdentifier, ZevEmailSnapshot = zev.ContactEmail,
                ZevPhoneSnapshot = zev.ContactPhone, PersonNameSnapshot = first.Person.DisplayName,
                PersonAddressSnapshot = first.Person.AddressLine, UnitCodeSnapshot = first.Unit.Code,
                UnitTypeSnapshot = first.Unit.Type.ToString(), UnitAreaSnapshot = first.Unit.AreaSquareMeters,
                UnitAddressSnapshot = first.Unit.Entrance.Building.AddressLine ?? zev.AddressLine,
                CreatedUtc = now, IssuedUtc = now, CreatedByUserId = actor, IssuedByUserId = actor,
                Currency = currency
            };
            foreach (var item in group)
            {
                var account = bankAccounts.FirstOrDefault(candidate => candidate.FundId == item.Line.FundId);
                item.Line.InvoiceId = invoice.Id;
                item.Line.BankAccountSnapshot = account?.AccountNumber;
                item.Line.BankNameSnapshot = account?.BankName;
                invoice.Lines.Add(item.Line);
            }
            invoices.Add(invoice);
        }
        foreach (var item in plan.Items)
        {
            item.Charge.BillingRunId = run.Id;
            item.Charge.Reference = $"ZEV-{item.Charge.Id:N}";
        }
        database.BillingRuns.Add(run);
        database.Charges.AddRange(plan.Items.Select(item => item.Charge));
        database.Invoices.AddRange(invoices);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            if (creditPlan is not null)
            {
                database.PaymentAllocations.AddRange(creditPlan.Allocations.Select(item => new PaymentAllocation
                {
                    OrganizationId = plan.OrganizationId, ZevId = zevId, PaymentId = item.PaymentId,
                    ChargeId = item.ChargeId, Amount = item.Amount, OperationId = run.Id
                }));
                var appliedPaymentIds = creditPlan.Allocations.Select(line => line.PaymentId).Distinct().ToArray();
                var bankIds = await database.Payments.AsNoTracking()
                    .Where(item => appliedPaymentIds.Contains(item.Id)
                        && item.BankTransactionId != null)
                    .Select(item => new { item.Id, BankId = item.BankTransactionId!.Value })
                    .ToArrayAsync(cancellationToken);
                var affectedBankIds = bankIds.Select(item => item.BankId).ToArray();
                foreach (var bank in await database.BankTransactions.Where(item => affectedBankIds.Contains(item.Id))
                    .ToListAsync(cancellationToken))
                {
                    var paymentId = bankIds.Single(item => item.BankId == bank.Id).Id;
                    bank.Status = creditPlan.RemainingByPayment.GetValueOrDefault(paymentId) == 0m
                        ? BankTransactionStatus.Matched : BankTransactionStatus.PartiallyMatched;
                }
                await database.SaveChangesAsync(cancellationToken);
                var importIds = await database.BankTransactions.AsNoTracking()
                    .Where(item => affectedBankIds.Contains(item.Id))
                    .Select(item => item.ImportId).Distinct().ToArrayAsync(cancellationToken);
                foreach (var import in await database.BankStatementImports.Where(item => importIds.Contains(item.Id))
                    .ToListAsync(cancellationToken))
                {
                    var statuses = await database.BankTransactions.AsNoTracking()
                        .Where(item => item.ImportId == import.Id).Select(item => item.Status)
                        .ToArrayAsync(cancellationToken);
                    import.Status = statuses.All(status => status is BankTransactionStatus.Matched
                            or BankTransactionStatus.Ignored or BankTransactionStatus.Returned)
                        ? BankImportStatus.Reconciled
                        : statuses.Any(status => status is BankTransactionStatus.Matched
                            or BankTransactionStatus.PartiallyMatched or BankTransactionStatus.Returned)
                            ? BankImportStatus.PartiallyReconciled : BankImportStatus.Imported;
                }
                await database.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new(run, BillingFailure.None);
        }
        catch (DbUpdateException problem) when (problem.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return new(null, BillingFailure.Conflict, Detail: "Billing run or invoice already exists.");
        }
    }

    private async Task<BillingPlan> BuildPlanAsync(Guid zevId, DateOnly period, DateOnly dueDate,
        BillingDraftRequest? draft, IReadOnlyCollection<Guid>? propertyUnitIds, CancellationToken cancellationToken)
    {
        var zev = await database.Zevs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == zevId
            && item.IsActive && item.Organization.IsActive, cancellationToken);
        if (zev is null) return new(null, BillingFailure.Missing, Guid.Empty, null,
            "Selected ZEV is not active or was not found.");
        var units = await database.PropertyUnits.AsNoTracking().Include(item => item.Entrance)
            .ThenInclude(item => item.Building).Where(item => item.ZevId == zevId && item.IsActive
            && item.Entrance.IsActive && item.Entrance.Building.IsActive).ToListAsync(cancellationToken);
        if (propertyUnitIds is not null)
        {
            var selectedUnitIds = propertyUnitIds.ToHashSet();
            if (selectedUnitIds.Count == 0 || selectedUnitIds.Count != propertyUnitIds.Count
                || units.Count(item => selectedUnitIds.Contains(item.Id)) != selectedUnitIds.Count)
                return new(null, BillingFailure.Invalid, zev.OrganizationId, null,
                    "Selected property unit is not active in selected ZEV.");
            units = units.Where(item => selectedUnitIds.Contains(item.Id)).ToList();
        }
        if (units.Count == 0) return new(null, BillingFailure.Conflict, zev.OrganizationId, null,
            "No active property units found.");
        var unitIds = units.Select(item => item.Id).ToArray();
        var owners = await database.Ownerships.AsNoTracking().Where(item => unitIds.Contains(item.PropertyUnitId)
            && item.ValidFrom <= period && (item.ValidTo == null || item.ValidTo > period)).ToListAsync(cancellationToken);
        var ownersByUnit = owners.ToLookup(item => item.PropertyUnitId);
        var unitWithoutOwner = units.FirstOrDefault(unit => !ownersByUnit.Contains(unit.Id));
        if (unitWithoutOwner is not null) return new(null, BillingFailure.Conflict, zev.OrganizationId, null,
            $"Property unit {unitWithoutOwner.Code} has no active owner for selected period.");
        var personIds = owners.Select(item => item.PersonId).Distinct().ToArray();
        var people = await database.People.AsNoTracking().Where(item => personIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var tariffs = await database.TariffVersions.AsNoTracking().Include(item => item.Fund)
            .Where(item => item.ZevId == zevId && item.IsActive
            && item.Basis != TariffBasis.Extraordinary && item.ValidFrom <= period
            && (item.ValidTo == null || item.ValidTo > period) && item.Fund.IsActive).ToListAsync(cancellationToken);
        tariffs = tariffs.Where(item => item.Basis != TariffBasis.OneTime || item.ValidFrom == period).ToList();
        if (draft?.TariffVersionIds is not null)
        {
            var referencedIds = draft.TariffVersionIds.Concat(draft.Adjustments?.Select(item => item.TariffVersionId) ?? [])
                .ToHashSet();
            var selectedCodes = tariffs.Where(item => referencedIds.Contains(item.Id))
                .Select(item => item.Code).ToHashSet();
            tariffs = tariffs.Where(item => selectedCodes.Contains(item.Code)).ToList();
        }
        var duplicateTariff = tariffs.GroupBy(item => item.Code).FirstOrDefault(group => group.Count() > 1);
        if (duplicateTariff is not null)
            return new(null, BillingFailure.Conflict, zev.OrganizationId, null,
                $"Multiple active tariff versions exist for code {duplicateTariff.Key}.");
        var tariffById = tariffs.ToDictionary(item => item.Id);
        var selected = draft?.TariffVersionIds is null
            ? tariffs.Where(item => !item.IsOptional && item.Basis != TariffBasis.ManualAmount).Select(item => item.Id).ToHashSet()
            : draft.TariffVersionIds.ToHashSet();
        if (selected.Any(id => !tariffById.ContainsKey(id)))
            return new(null, BillingFailure.Invalid, zev.OrganizationId, null,
                "Selected tariff is not valid for selected period.");
        if (selected.Count == 0 && (draft?.Adjustments?.Count ?? 0) == 0
            && (draft?.ManualLines?.Count ?? 0) == 0)
            return new(null, BillingFailure.Invalid, zev.OrganizationId, null,
                "No billing items are selected for the period.");
        var adjustments = draft?.Adjustments ?? [];
        if (adjustments.GroupBy(item => new { item.PropertyUnitId, item.TariffVersionId }).Any(group => group.Count() > 1)
            || adjustments.Any(item => !unitIds.Contains(item.PropertyUnitId) || !tariffById.ContainsKey(item.TariffVersionId)
                || item.Note?.Length > 1000 || item.Reason?.Length > 500
                || item.Description?.Length > 200 || item.UnitOfMeasure?.Length > 30
                || item.Description is not null && string.IsNullOrWhiteSpace(item.Description)
                || item.UnitOfMeasure is not null && string.IsNullOrWhiteSpace(item.UnitOfMeasure)
                || item.ManualAmount is < 0m or > 99999999999999m
                || item.ManualAmount is not null && (item.Quantity is not null || item.UnitPrice is not null)
                || item.Quantity is <= 0m or > 999999999999999m
                || item.Quantity is decimal quantity && decimal.Round(quantity, 3) != quantity
                || item.UnitPrice is < 0m or > 99999999999999m
                || item.UnitPrice is decimal unitPrice && decimal.Round(unitPrice, 4) != unitPrice
                || item.DiscountValue is < 0m or > 99999999999999m
                || item.DiscountValue is decimal discountValue && decimal.Round(discountValue, 4) != discountValue
                || item.DiscountType is not null && !Enum.TryParse<InvoiceDiscountType>(item.DiscountType, true, out _)))
            return new(null, BillingFailure.Invalid, zev.OrganizationId, null);
        var adjustmentByPair = adjustments.ToDictionary(item => (item.PropertyUnitId, item.TariffVersionId));
        var items = new List<PlannedCharge>(units.Count * tariffs.Count);
        foreach (var unit in units)
        {
            foreach (var tariff in tariffs)
            {
                adjustmentByPair.TryGetValue((unit.Id, tariff.Id), out var adjustment);
                var include = adjustment?.Include ?? selected.Contains(tariff.Id);
                if (adjustment?.Include is false && !tariff.IsOptional || adjustment?.Include is true
                    && !selected.Contains(tariff.Id) && !tariff.IsOptional)
                    return new(null, BillingFailure.Invalid, zev.OrganizationId, null);
                if (!include) continue;
                if (tariff.Basis == TariffBasis.ManualAmount && adjustment?.ManualAmount is null
                    || adjustment?.ManualAmount is not null && tariff.Basis != TariffBasis.ManualAmount
                    && string.IsNullOrWhiteSpace(adjustment.Reason))
                    return new(null, BillingFailure.Invalid, zev.OrganizationId, null);
                var quantity = adjustment?.Quantity ?? BillingCalculator.Quantity(tariff.Basis, unit);
                if (quantity == 0m) continue;
                var rate = adjustment?.ManualAmount ?? adjustment?.UnitPrice ?? tariff.Rate;
                var effectiveQuantity = adjustment?.ManualAmount is not null ? 1m : quantity;
                var unitAmount = BillingCalculator.Amount(effectiveQuantity, rate, 1m);
                if (unitAmount <= 0m) continue;
                var discountType = Enum.TryParse<InvoiceDiscountType>(adjustment?.DiscountType, true, out var parsedDiscount)
                    ? parsedDiscount : InvoiceDiscountType.None;
                var discountValue = adjustment?.DiscountValue ?? 0m;
                if (discountType == InvoiceDiscountType.Percentage && discountValue > 100m
                    || discountType == InvoiceDiscountType.Fixed && discountValue > unitAmount
                    || discountType == InvoiceDiscountType.None && discountValue != 0m
                    || discountType != InvoiceDiscountType.None && discountValue == 0m)
                    return new(null, BillingFailure.Invalid, zev.OrganizationId, null, "Invalid discount for billing item.");
                if ((adjustment?.Quantity is not null || adjustment?.UnitPrice is not null
                    || discountType != InvoiceDiscountType.None) && string.IsNullOrWhiteSpace(adjustment?.Reason))
                    return new(null, BillingFailure.Invalid, zev.OrganizationId, null, "A reason is required for a manual adjustment.");
                foreach (var owner in ownersByUnit[unit.Id])
                {
                    if (!people.TryGetValue(owner.PersonId, out var person))
                        return new(null, BillingFailure.Conflict, zev.OrganizationId, null);
                    var gross = BillingCalculator.Amount(effectiveQuantity, rate, owner.Share);
                    if (gross == 0m) continue;
                    var discount = discountType switch
                    {
                        InvoiceDiscountType.Percentage => BillingCalculator.Amount(gross, discountValue / 100m, 1m),
                        InvoiceDiscountType.Fixed => BillingCalculator.Amount(1m, discountValue, owner.Share),
                        _ => 0m
                    };
                    var amount = gross - discount;
                    if (amount < 0m) return new(null, BillingFailure.Invalid, zev.OrganizationId, null,
                        "Discount exceeds the billing item amount.");
                    var note = string.IsNullOrWhiteSpace(adjustment?.Reason) ? adjustment?.Note?.Trim()
                        : $"Manual override: {adjustment.Reason.Trim()}. {adjustment.Note?.Trim()}".Trim();
                    if (note?.Length > 1000) return new(null, BillingFailure.Invalid, zev.OrganizationId, null);
                    var charge = new Charge
                    {
                        OrganizationId = zev.OrganizationId, ZevId = zevId, PropertyUnitId = unit.Id,
                        PersonId = owner.PersonId, FundId = tariff.FundId, TariffVersionId = tariff.Id,
                        Period = period, DueDate = dueDate, BasisQuantity = effectiveQuantity, RateSnapshot = rate,
                        OwnershipShareSnapshot = owner.Share, Amount = amount, Note = note,
                        IsManualOverride = adjustment?.ManualAmount is not null || adjustment?.UnitPrice is not null
                            || adjustment?.Quantity is not null || discountType != InvoiceDiscountType.None
                    };
                    var line = new InvoiceLine
                    {
                        OrganizationId = zev.OrganizationId, ZevId = zevId, ChargeId = charge.Id,
                        TariffVersionId = tariff.Id, FundId = tariff.FundId,
                        CodeSnapshot = tariff.Code, DescriptionSnapshot = adjustment?.Description?.Trim() ?? tariff.Name,
                        UnitOfMeasureSnapshot = adjustment?.UnitOfMeasure?.Trim() ?? tariff.Basis switch
                        {
                            TariffBasis.SquareMeter => "m2", TariffBasis.RegisteredMember => "member", _ => "unit"
                        },
                        Quantity = effectiveQuantity, UnitPrice = rate, OwnershipShareSnapshot = owner.Share,
                        DiscountType = discountType, DiscountValue = discountValue, DiscountAmount = discount,
                        AmountBeforeDiscount = gross, FinalAmount = amount, SortOrder = tariff.DisplayOrder,
                        ShowOnInvoice = tariff.ShowOnInvoice, NoteSnapshot = note, FundNameSnapshot = tariff.Fund.Name
                    };
                    items.Add(new(charge, line, tariff, tariff.Fund, unit, person));
                }
            }
        }
        var manualLines = draft?.ManualLines ?? [];
        if (manualLines.Select(item => item.Id).Distinct().Count() != manualLines.Count
            || manualLines.Any(item => item.Id == Guid.Empty || !unitIds.Contains(item.PropertyUnitId)
                || string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 200
                || string.IsNullOrWhiteSpace(item.UnitOfMeasure) || item.UnitOfMeasure.Length > 30
                || string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > 500
                || item.Note?.Length > 1000 || item.Quantity <= 0m || item.Quantity > 999999999999999m
                || decimal.Round(item.Quantity, 3) != item.Quantity || item.UnitPrice < 0m
                || item.UnitPrice > 99999999999999m || decimal.Round(item.UnitPrice, 4) != item.UnitPrice
                || item.DiscountValue is < 0m or > 99999999999999m
                || item.DiscountValue is decimal discount && decimal.Round(discount, 4) != discount
                || item.DiscountType is not null && !Enum.TryParse<InvoiceDiscountType>(item.DiscountType, true, out _)))
            return new(null, BillingFailure.Invalid, zev.OrganizationId, null, "Invalid free manual billing line.");
        var manualFundIds = manualLines.Select(item => item.FundId).Distinct().ToArray();
        var manualFunds = await database.Funds.AsNoTracking().Where(item => item.ZevId == zevId
            && manualFundIds.Contains(item.Id) && item.IsActive).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (manualFundIds.Any(item => !manualFunds.ContainsKey(item)))
            return new(null, BillingFailure.Invalid, zev.OrganizationId, null, "Manual line requires an active fund in selected ZEV.");
        foreach (var manual in manualLines.OrderBy(item => item.Id))
        {
            var unit = units.Single(item => item.Id == manual.PropertyUnitId);
            var fund = manualFunds[manual.FundId];
            var discountType = Enum.TryParse<InvoiceDiscountType>(manual.DiscountType, true, out var parsed)
                ? parsed : InvoiceDiscountType.None;
            var discountValue = manual.DiscountValue ?? 0m;
            if (manual.UnitPrice > 0m && manual.Quantity > 9999999999999999.99m / manual.UnitPrice)
                return new(null, BillingFailure.Invalid, zev.OrganizationId, null, "Manual line amount exceeds supported money precision.");
            var fullGross = BillingCalculator.Amount(manual.Quantity, manual.UnitPrice, 1m);
            if (discountType == InvoiceDiscountType.Percentage && discountValue > 100m
                || discountType == InvoiceDiscountType.Fixed && discountValue > fullGross
                || discountType == InvoiceDiscountType.None && discountValue != 0m
                || discountType != InvoiceDiscountType.None && discountValue == 0m)
                return new(null, BillingFailure.Invalid, zev.OrganizationId, null, "Invalid manual line discount.");
            var note = $"Manual line ({manual.Id:N}); reason: {manual.Reason.Trim()}; {manual.Note?.Trim()}".Trim();
            if (note.Length > 1000)
                return new(null, BillingFailure.Invalid, zev.OrganizationId, null, "Manual line reason and note are too long.");
            foreach (var owner in ownersByUnit[unit.Id])
            {
                if (!people.TryGetValue(owner.PersonId, out var person))
                    return new(null, BillingFailure.Conflict, zev.OrganizationId, null);
                var gross = BillingCalculator.Amount(manual.Quantity, manual.UnitPrice, owner.Share);
                var discount = discountType switch
                {
                    InvoiceDiscountType.Percentage => BillingCalculator.Amount(gross, discountValue / 100m, 1m),
                    InvoiceDiscountType.Fixed => BillingCalculator.Amount(1m, discountValue, owner.Share),
                    _ => 0m
                };
                var amount = gross - discount;
                if (amount < 0m) return new(null, BillingFailure.Invalid, zev.OrganizationId, null,
                    "Discount exceeds manual line amount.");
                var charge = new Charge
                {
                    OrganizationId = zev.OrganizationId, ZevId = zevId, PropertyUnitId = unit.Id,
                    PersonId = owner.PersonId, FundId = fund.Id, TariffVersionId = null,
                    Period = period, DueDate = dueDate, BasisQuantity = manual.Quantity,
                    RateSnapshot = manual.UnitPrice, OwnershipShareSnapshot = owner.Share,
                    Amount = amount, Note = note, IsManualOverride = true
                };
                var line = new InvoiceLine
                {
                    OrganizationId = zev.OrganizationId, ZevId = zevId, ChargeId = charge.Id,
                    TariffVersionId = null, FundId = fund.Id, CodeSnapshot = "MANUAL",
                    DescriptionSnapshot = manual.Description.Trim(),
                    UnitOfMeasureSnapshot = manual.UnitOfMeasure.Trim(), Quantity = manual.Quantity,
                    UnitPrice = manual.UnitPrice, OwnershipShareSnapshot = owner.Share,
                    DiscountType = discountType, DiscountValue = discountValue, DiscountAmount = discount,
                    AmountBeforeDiscount = gross, FinalAmount = amount, SortOrder = int.MaxValue,
                    ShowOnInvoice = true, NoteSnapshot = note, FundNameSnapshot = fund.Name
                };
                items.Add(new(charge, line, null, fund, unit, person, manual.Id));
            }
        }
        if (items.Count == 0) return new(null, BillingFailure.Invalid, zev.OrganizationId, null);
        return new(items, BillingFailure.None, zev.OrganizationId, Fingerprint(zevId, period, dueDate, items));
    }

    private static string Fingerprint(Guid zevId, DateOnly period, DateOnly dueDate, List<PlannedCharge> items)
    {
        var values = new List<string> { zevId.ToString("N"), period.ToString("O"), dueDate.ToString("O") };
        values.AddRange(items.OrderBy(item => item.Unit.Id).ThenBy(item => item.Person.Id)
            .ThenBy(item => item.Tariff?.Id ?? item.ManualLineId)
            .Select(item => string.Join("|", item.Unit.Id, item.Person.Id, item.Tariff?.Id, item.ManualLineId,
                item.Charge.BasisQuantity.ToString(CultureInfo.InvariantCulture),
                item.Charge.RateSnapshot.ToString(CultureInfo.InvariantCulture),
                item.Charge.Amount.ToString(CultureInfo.InvariantCulture), item.Charge.Note,
                item.Charge.IsManualOverride, item.Line.DescriptionSnapshot, item.Line.UnitOfMeasureSnapshot,
                item.Line.DiscountType, item.Line.DiscountValue.ToString(CultureInfo.InvariantCulture),
                item.Line.DiscountAmount.ToString(CultureInfo.InvariantCulture), item.Line.FundId)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values))));
    }

    private async Task<BillingCreditPlan> PlanCreditAsync(Guid zevId, BillingPlan plan, string currency,
        CancellationToken cancellationToken)
    {
        var items = plan.Items!;
        var personIds = items.Select(item => item.Person.Id).Distinct().ToArray();
        var fundIds = items.Select(item => item.Fund.Id).Distinct().ToArray();
        var legacyCurrency = configuration?["Finance:DefaultCurrency"]?.Trim().ToUpperInvariant() ?? "BAM";
        var payments = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId
                && item.Status == PaymentStatus.Posted && item.PersonId != null
                && personIds.Contains(item.PersonId.Value) && fundIds.Contains(item.FundId)
                && item.CurrencyCode == currency)
            .Select(item => new { item.Id, PersonId = item.PersonId!.Value, item.FundId,
                item.PaidOn, item.Amount,
                Available = item.Amount - (database.PaymentAllocations.Where(allocation =>
                    allocation.PaymentId == item.Id).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) })
            .Where(item => item.Available > 0m)
            .OrderBy(item => item.PaidOn).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var existing = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
                && item.Amount > 0m && personIds.Contains(item.PersonId) && fundIds.Contains(item.FundId)
                && (database.InvoiceLines.Any(line => line.ChargeId == item.Id
                        && line.Invoice.Currency == currency)
                    || !database.InvoiceLines.Any(line => line.ChargeId == item.Id)
                        && (database.BankAccounts.Any(account => account.ZevId == zevId
                                && account.FundId == item.FundId && account.IsActive
                                && account.CurrencyCode == currency)
                            || !database.BankAccounts.Any(account => account.ZevId == zevId
                                && account.FundId == item.FundId && account.IsActive)
                                && currency == legacyCurrency)))
            .Select(item => new { item.Id, item.PersonId, item.FundId, item.PropertyUnitId,
                item.DueDate, item.Period,
                Open = item.Amount + (database.Charges.Where(reversal => reversal.ReversesChargeId == item.Id)
                    .Sum(reversal => (decimal?)reversal.Amount) ?? 0m)
                    - (database.PaymentAllocations.Where(allocation => allocation.ChargeId == item.Id
                        && allocation.Payment.Status == PaymentStatus.Posted)
                        .Sum(allocation => (decimal?)allocation.Amount) ?? 0m) })
            .Where(item => item.Open > 0m).ToListAsync(cancellationToken);
        var candidates = existing.Select(item => new CreditCandidate(item.Id, item.PersonId, item.FundId,
            item.PropertyUnitId, item.DueDate, item.Period, item.Id.ToString("N"), true, item.Open))
            .Concat(items.Select(item => new CreditCandidate(item.Charge.Id, item.Person.Id, item.Fund.Id,
                item.Unit.Id, item.Charge.DueDate, item.Charge.Period,
                $"{item.Unit.Id:N}|{item.Line.CodeSnapshot}|{item.ManualLineId:N}", false, item.Charge.Amount)))
            .OrderBy(item => item.DueDate).ThenBy(item => item.Period)
            .ThenByDescending(item => item.Existing).ThenBy(item => item.SortKey, StringComparer.Ordinal)
            .ToList();
        var hashInput = new List<string> { plan.Fingerprint ?? "", currency };
        hashInput.AddRange(payments.Select(item => string.Join("|", item.Id,
            item.Available.ToString(CultureInfo.InvariantCulture))));
        hashInput.AddRange(existing.OrderBy(item => item.Id).Select(item => string.Join("|", item.Id,
            item.Open.ToString(CultureInfo.InvariantCulture))));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", hashInput))));
        var allocations = new List<BillingCreditAllocation>();
        var remainingByPayment = new Dictionary<Guid, decimal>();
        foreach (var payment in payments)
        {
            var remaining = payment.Available;
            foreach (var charge in candidates.Where(item => item.PersonId == payment.PersonId
                && item.FundId == payment.FundId && item.Remaining > 0m))
            {
                var amount = Math.Min(remaining, charge.Remaining);
                if (amount <= 0m) break;
                allocations.Add(new(payment.Id, charge.Id, charge.PropertyUnitId, charge.Existing, amount));
                charge.Remaining -= amount;
                remaining -= amount;
                if (remaining == 0m) break;
            }
            remainingByPayment[payment.Id] = remaining;
        }
        return new(hash, allocations, remainingByPayment);
    }

    private static BillingPreviewResult Empty(BillingFailure failure, Guid? existingRunId = null,
        string? detail = null, IReadOnlyList<BillingPreflightIssue>? issues = null) =>
        new(failure, null, [], 0m, 0, existingRunId) { Detail = detail, Issues = issues ?? [] };

    private async Task<List<BillingPreflightIssue>> BankAccountIssuesAsync(Guid zevId,
        List<PlannedCharge> items, CancellationToken cancellationToken)
    {
        var required = items.Select(item => item.Fund).Where(item => item.RequiresBankAccountForInvoice)
            .DistinctBy(item => item.Id).ToList();
        if (required.Count == 0) return [];
        var accountFundIds = (await database.BankAccounts.AsNoTracking()
            .Where(item => item.ZevId == zevId && item.IsActive)
            .Select(item => item.FundId).ToListAsync(cancellationToken)).ToHashSet();
        return required.Where(item => !accountFundIds.Contains(item.Id))
            .Select(item => new BillingPreflightIssue("FUND_BANK_ACCOUNT_MISSING",
                $"Fond '{item.Name}' nema aktivan bankovni račun. Račun se ne može izdati dok se ne podese podaci za plaćanje.",
                BillingFailure.Conflict, FundName: item.Name)).ToList();
    }

    private sealed record PlannedCharge(Charge Charge, InvoiceLine Line, TariffVersion? Tariff,
        Fund Fund, PropertyUnit Unit, Person Person, Guid? ManualLineId = null);
    private sealed record BillingPlan(List<PlannedCharge>? Items, BillingFailure Failure,
        Guid OrganizationId, string? Fingerprint, string? Detail = null);
    private sealed record BillingCreditPlan(string Hash, List<BillingCreditAllocation> Allocations,
        Dictionary<Guid, decimal> RemainingByPayment);
    private sealed class CreditCandidate(Guid id, Guid personId, Guid fundId, Guid propertyUnitId,
        DateOnly dueDate, DateOnly period, string sortKey, bool existing, decimal remaining)
    {
        public Guid Id { get; } = id;
        public Guid PersonId { get; } = personId;
        public Guid FundId { get; } = fundId;
        public Guid PropertyUnitId { get; } = propertyUnitId;
        public DateOnly DueDate { get; } = dueDate;
        public DateOnly Period { get; } = period;
        public string SortKey { get; } = sortKey;
        public bool Existing { get; } = existing;
        public decimal Remaining { get; set; } = remaining;
    }
}
