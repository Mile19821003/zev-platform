using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Payments;

namespace Zev.Api.Tests;

public sealed class BillingTests
{
    [Fact]
    public async Task MultipleBillingCurrenciesFailPreflightPreviewAndPostBeforeCreditPlanning()
    {
        using var fixture = new BillingFixture(1);
        fixture.Zev.AutoApplyPaymentCredit = true;
        var reserve = new Fund { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, Kind = FundKind.Reserve, Name = "Euro reserve" };
        var reserveTariff = new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, Fund = reserve, Code = "EUR-RESERVE", Name = "Euro reserve",
            Version = 1, Basis = TariffBasis.FixedUnit, Rate = 5m,
            ValidFrom = new DateOnly(2026, 1, 1) };
        fixture.Database.AddRange(reserve, reserveTariff,
            new BankAccount { OrganizationId = fixture.Zev.OrganizationId, ZevId = fixture.Zev.Id,
                FundId = fixture.Tariff.FundId, AccountNumber = "BAM-ACCOUNT", CurrencyCode = "BAM" },
            new BankAccount { OrganizationId = fixture.Zev.OrganizationId, ZevId = fixture.Zev.Id,
                Fund = reserve, AccountNumber = "EUR-ACCOUNT", CurrencyCode = "EUR" });
        await fixture.Database.SaveChangesAsync();
        var period = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 10, 30);
        var preflight = await fixture.Engine.PreflightAsync(fixture.Zev.Id, period, dueDate,
            null, CancellationToken.None);
        Assert.Contains(preflight.Issues, item => item.Code == "BILLING_MULTIPLE_CURRENCIES");
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, dueDate,
            null, CancellationToken.None);
        Assert.Equal(BillingFailure.Conflict, preview.Failure);
        Assert.Contains("multiple currencies", preview.Detail);
        var posted = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, dueDate,
            CancellationToken.None);
        Assert.Equal(BillingFailure.Conflict, posted.Failure);
        Assert.Empty(fixture.Database.Invoices);
    }

    [Fact]
    public async Task AutoCreditPreviewAndPostUseSamePlanAndKeepInvoiceSnapshot()
    {
        var actorId = Guid.NewGuid();
        using var fixture = new BillingFixture(1, actorId);
        fixture.Zev.AutoApplyPaymentCredit = true;
        await fixture.Database.SaveChangesAsync();
        var personId = fixture.Database.People.Single().Id;
        var fundId = fixture.Database.Funds.Single().Id;
        var credit = await new PaymentWorkflowService(fixture.Database).CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), personId, null, fundId,
                new DateOnly(2026, 9, 1), 20m, "BAM", "Migration", "Opening credit", null, null, null),
            null, CancellationToken.None);
        Assert.True(credit.Success, credit.Error);
        var line = new BillingManualLine(Guid.NewGuid(), fixture.Units[0].Id, fundId,
            "Maintenance", "kom", 1m, 50m, null, null, null, "Board decision");
        var draft = new BillingDraftRequest([], null, null, [line]);
        var period = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 10, 30);
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, dueDate,
            draft, CancellationToken.None);
        Assert.Equal(50m, preview.TotalAmount);
        Assert.Equal(20m, preview.ProposedCreditTotal);
        Assert.Equal(30m, preview.RemainingToPay);
        Assert.Single(preview.CreditAllocations);
        var posted = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, dueDate,
            draft with { ExpectedHash = preview.Fingerprint,
                ExpectedCreditPlanHash = preview.CreditPlanHash }, CancellationToken.None);
        Assert.Equal(BillingFailure.None, posted.Failure);
        var invoice = Assert.Single(fixture.Database.Invoices);
        Assert.Equal(50m, invoice.TotalAmount);
        Assert.Equal(50m, invoice.TotalDueAtIssue);
        Assert.Equal(20m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal(30m, invoice.TotalAmount - fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Single(fixture.Database.Payments);
        Assert.Contains(fixture.Database.AuditLogs, item => item.EntityType == nameof(PaymentAllocation)
            && item.Action == "Added" && item.ActorUserId == actorId && item.OccurredUtc != default);
    }

    [Fact]
    public async Task AutoCreditRejectsStalePreviewBeforePosting()
    {
        using var fixture = new BillingFixture(1);
        fixture.Zev.AutoApplyPaymentCredit = true;
        await fixture.Database.SaveChangesAsync();
        var personId = fixture.Database.People.Single().Id;
        var fundId = fixture.Database.Funds.Single().Id;
        var draft = new BillingDraftRequest([fixture.Tariff.Id], null, null);
        var period = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 10, 30);
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, dueDate,
            draft, CancellationToken.None);
        var credit = await new PaymentWorkflowService(fixture.Database).CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), personId, null, fundId,
                new DateOnly(2026, 9, 1), 20m, "BAM", "Migration", "Opening credit", null, null, null),
            null, CancellationToken.None);
        Assert.True(credit.Success, credit.Error);
        var posted = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, dueDate,
            draft with { ExpectedHash = preview.Fingerprint,
                ExpectedCreditPlanHash = preview.CreditPlanHash }, CancellationToken.None);
        Assert.Equal(BillingFailure.Conflict, posted.Failure);
        Assert.Empty(fixture.Database.Invoices);
        Assert.Empty(fixture.Database.BillingRuns);
    }
    [Fact]
    public async Task FreeManualLinesWithoutTariffsPreviewAndPostWithAuditableReason()
    {
        using var fixture = new BillingFixture(1);
        var fund = fixture.Database.Funds.Single();
        var manual = new BillingManualLine(Guid.NewGuid(), fixture.Units[0].Id, fund.Id,
            "Poseban rad", "sat", 2m, 10m, "Fixed", 3m, "Upravnik odobrio", "Odluka ZEV-a");
        var draft = new BillingDraftRequest([], null, null, [manual]);
        var period = new DateOnly(2026, 10, 1);
        var due = new DateOnly(2026, 10, 30);
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, due, draft, CancellationToken.None);
        Assert.Equal(BillingFailure.None, preview.Failure);
        Assert.Equal(17m, preview.TotalAmount);
        Assert.Null(Assert.Single(Assert.Single(preview.Units).Lines).TariffVersionId);
        var posted = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, due,
            draft with { ExpectedHash = preview.Fingerprint }, CancellationToken.None);
        Assert.Equal(BillingFailure.None, posted.Failure);
        var charge = Assert.Single(fixture.Database.Charges);
        Assert.Null(charge.TariffVersionId);
        Assert.True(charge.IsManualOverride);
        Assert.Contains("Odluka ZEV-a", charge.Note);
        var line = Assert.Single(fixture.Database.InvoiceLines);
        Assert.Null(line.TariffVersionId);
        Assert.Equal("Poseban rad", line.DescriptionSnapshot);
        Assert.Equal(3m, line.DiscountAmount);
        Assert.Equal(17m, line.FinalAmount);
    }

    [Fact]
    public async Task MultipleFreeLinesForSameUnitAreDistinctAndRequireReasons()
    {
        using var fixture = new BillingFixture(1);
        var fund = fixture.Database.Funds.Single();
        var first = new BillingManualLine(Guid.NewGuid(), fixture.Units[0].Id, fund.Id,
            "Prvi rad", "sat", 1m, 10m, null, null, null, "Odluka 1");
        var second = first with { Id = Guid.NewGuid(), Description = "Drugi rad", UnitPrice = 7m,
            Reason = "Odluka 2" };
        var period = new DateOnly(2026, 10, 1);
        var due = new DateOnly(2026, 10, 30);
        var invalid = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, due,
            new BillingDraftRequest([], null, null, [first with { Reason = "" }]), CancellationToken.None);
        Assert.Equal(BillingFailure.Invalid, invalid.Failure);
        var draft = new BillingDraftRequest([], null, null, [first, second]);
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, due, draft, CancellationToken.None);
        Assert.Equal(17m, preview.TotalAmount);
        Assert.Equal(2, preview.ChargeCount);
        Assert.Equal(BillingFailure.None, (await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, due,
            draft with { ExpectedHash = preview.Fingerprint }, CancellationToken.None)).Failure);
        Assert.Equal(2, fixture.Database.InvoiceLines.Count());
        Assert.Equal(2, fixture.Database.Charges.Count(item => item.TariffVersionId == null));
    }

    [Fact]
    public async Task RequiredFundAccountBlocksPreviewAndPostWithoutMutatingData()
    {
        using var fixture = new BillingFixture(1);
        fixture.Database.Funds.Single().RequiresBankAccountForInvoice = true;
        fixture.Database.SaveChanges();
        var period = new DateOnly(2026, 10, 1);
        var due = new DateOnly(2026, 10, 30);
        var preflight = await fixture.Engine.PreflightAsync(fixture.Zev.Id, period, due, null, CancellationToken.None);
        Assert.Contains(preflight.Issues, item => item.Code == "FUND_BANK_ACCOUNT_MISSING");
        Assert.Equal(BillingFailure.Conflict, (await fixture.Engine.GenerateAsync(fixture.Zev.Id,
            period, due, CancellationToken.None)).Failure);
        Assert.Empty(fixture.Database.BillingRuns);
        Assert.Empty(fixture.Database.Invoices);
        var fund = fixture.Database.Funds.Single();
        fixture.Database.BankAccounts.Add(new BankAccount { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fund.Id, AccountNumber = "TEST-ACCOUNT", CurrencyCode = "BAM" });
        fixture.Database.SaveChanges();
        Assert.True((await fixture.Engine.PreflightAsync(fixture.Zev.Id, period, due, null,
            CancellationToken.None)).CanPreview);
        Assert.Equal(BillingFailure.None, (await fixture.Engine.GenerateAsync(fixture.Zev.Id,
            period, due, CancellationToken.None)).Failure);
    }

    [Fact]
    public void InvoiceNumberProviderFormatsConfiguredPrefix()
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Finance:InvoiceNumberPrefix"] = "zev" }).Build();
        Assert.Equal("ZEV-2026-000007", new InvoiceNumberProvider(settings).Format(2026, 7));
    }

    [Fact]
    public void CalculatesSquareMeterFixedAndMemberTariffsWithDecimalMoney()
    {
        var unit = new PropertyUnit { AreaSquareMeters = 54.375m, RegisteredMembers = 3 };
        Assert.Equal(54.375m, BillingCalculator.Quantity(TariffBasis.SquareMeter, unit));
        Assert.Equal(1m, BillingCalculator.Quantity(TariffBasis.FixedUnit, unit));
        Assert.Equal(3m, BillingCalculator.Quantity(TariffBasis.RegisteredMember, unit));
        Assert.Equal(81.56m, BillingCalculator.Amount(54.375m, 1.50m, 1m));
        Assert.Equal(5m, BillingCalculator.Amount(1m, 10m, .5m));
    }

    [Fact]
    public async Task MonthlyRunIsIdempotentAndUsesTariffSnapshot()
    {
        using var fixture = new BillingFixture(2);
        var first = await fixture.Engine.GenerateAsync(fixture.Zev.Id, new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 20), CancellationToken.None);
        var second = await fixture.Engine.GenerateAsync(fixture.Zev.Id, new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 20), CancellationToken.None);
        Assert.Equal(BillingFailure.None, first.Failure);
        Assert.Equal(first.Run?.Id, second.Run?.Id);
        Assert.Equal(2, fixture.Database.Charges.Count());
        Assert.All(fixture.Database.Charges, charge => Assert.Equal(60m, charge.Amount));

        fixture.Tariff.Rate = 3m;
        fixture.Database.SaveChanges();
        Assert.All(fixture.Database.Charges, charge => Assert.Equal(2m, charge.RateSnapshot));
        Assert.Equal(2, fixture.Database.Invoices.Count());
        Assert.Equal(2, fixture.Database.InvoiceLines.Count());
        Assert.All(fixture.Database.InvoiceLines, line => Assert.Contains(fixture.Database.Charges,
            charge => charge.Id == line.ChargeId));
    }

    [Fact]
    public async Task IssuedInvoiceRetainsMasterAndBalanceSnapshotsAfterLaterChanges()
    {
        using var fixture = new BillingFixture(1);
        var first = await fixture.Engine.GenerateAsync(fixture.Zev.Id, new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 20), CancellationToken.None);
        Assert.Equal(BillingFailure.None, first.Failure);
        var second = await fixture.Engine.GenerateAsync(fixture.Zev.Id, new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 20), CancellationToken.None);
        Assert.Equal(BillingFailure.None, second.Failure);
        var october = Assert.Single(fixture.Database.Invoices.Where(item => item.Period.Month == 10));
        Assert.Equal(60m, october.PreviousBalanceSnapshot);
        Assert.Equal(120m, october.TotalDueAtIssue);
        Assert.Equal(2, october.NumberSequence);
        Assert.NotEqual(october.InvoiceNumber,
            Assert.Single(fixture.Database.Invoices.Where(item => item.Period.Month == 9)).InvoiceNumber);
        var line = Assert.Single(october.Lines);
        Assert.Equal(60m, line.FinalAmount);
        fixture.Zev.Name = "Renamed";
        fixture.Tariff.Name = "Changed tariff";
        fixture.Tariff.Rate = 9m;
        fixture.Units[0].Code = "RENAMED";
        fixture.Units[0].AreaSquareMeters = 90m;
        fixture.Database.SaveChanges();
        Assert.Equal("ZEV", october.ZevNameSnapshot);
        Assert.Equal("U-0", october.UnitCodeSnapshot);
        Assert.Equal(30m, october.UnitAreaSnapshot);
        Assert.Equal("Area", line.DescriptionSnapshot);
        Assert.Equal(2m, line.UnitPrice);
        Assert.Equal(60m, october.PreviousBalanceSnapshot);
    }

    [Fact]
    public async Task PercentageDiscountUsesDecimalRoundingAndPersistsInvoiceLine()
    {
        using var fixture = new BillingFixture(1);
        fixture.Units[0].AreaSquareMeters = 79.51m;
        fixture.Tariff.Rate = .50m;
        fixture.Database.SaveChanges();
        var period = new DateOnly(2026, 11, 1);
        var due = new DateOnly(2026, 11, 20);
        var draft = new BillingDraftRequest([fixture.Tariff.Id],
            [new BillingDraftAdjustment(fixture.Units[0].Id, fixture.Tariff.Id, true, null, null,
                "Approved discount", DiscountType: "Percentage", DiscountValue: 10m)], null);
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, due, draft, CancellationToken.None);
        var previewLine = Assert.Single(Assert.Single(preview.Units).Lines);
        Assert.Equal(39.76m, previewLine.GrossAmount);
        Assert.Equal(3.98m, previewLine.DiscountAmount);
        Assert.Equal(35.78m, previewLine.Amount);
        var posted = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, due,
            draft with { ExpectedHash = preview.Fingerprint }, CancellationToken.None);
        Assert.Equal(BillingFailure.None, posted.Failure);
        var invoice = Assert.Single(fixture.Database.Invoices);
        Assert.Equal(35.78m, invoice.TotalAmount);
        Assert.Equal(3.98m, invoice.DiscountTotal);
        var line = Assert.Single(fixture.Database.InvoiceLines);
        Assert.Equal(InvoiceDiscountType.Percentage, line.DiscountType);
        Assert.Equal(39.76m, line.AmountBeforeDiscount);
        Assert.Equal(35.78m, line.FinalAmount);
        Assert.Equal(35.78m, Assert.Single(fixture.Database.Charges).Amount);
    }

    [Fact]
    public async Task CoOwnersReceiveSeparateInvoicesAndFixedDiscountsFollowShare()
    {
        using var fixture = new BillingFixture(1);
        var original = Assert.Single(fixture.Database.Ownerships);
        original.Share = .5m;
        var other = new Person { OrganizationId = fixture.Zev.OrganizationId, DisplayName = "Second owner" };
        fixture.Database.Add(other);
        fixture.Database.Ownerships.Add(new Ownership { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, PropertyUnitId = fixture.Units[0].Id, PersonId = other.Id,
            ValidFrom = new DateOnly(2025, 1, 1), Share = .5m });
        fixture.Database.SaveChanges();
        var period = new DateOnly(2026, 12, 1);
        var due = new DateOnly(2026, 12, 20);
        var draft = new BillingDraftRequest([fixture.Tariff.Id],
            [new BillingDraftAdjustment(fixture.Units[0].Id, fixture.Tariff.Id, true, null, null,
                "Approved", DiscountType: "Fixed", DiscountValue: 10m)], null);
        var result = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, due, draft, CancellationToken.None);
        Assert.Equal(BillingFailure.None, result.Failure);
        Assert.Equal(2, fixture.Database.Invoices.Count());
        Assert.All(fixture.Database.InvoiceLines, line =>
        {
            Assert.Equal(30m, line.AmountBeforeDiscount);
            Assert.Equal(5m, line.DiscountAmount);
            Assert.Equal(25m, line.FinalAmount);
        });
    }

    [Fact]
    public async Task BulkRunCreatesOverThousandChargesWithinThirtySecondsInMemory()
    {
        using var fixture = new BillingFixture(1001);
        var watch = Stopwatch.StartNew();
        var result = await fixture.Engine.GenerateAsync(fixture.Zev.Id, new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 20), CancellationToken.None);
        watch.Stop();
        Assert.Equal(BillingFailure.None, result.Failure);
        Assert.Equal(1001, result.Run?.ChargeCount);
        Assert.Equal(1001, fixture.Database.Invoices.Count());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"In-memory run took {watch.Elapsed}.");
    }

    [Fact]
    public async Task PreviewOfThreeConfigurableItemsTotalsFiftyThreeWithoutWritingAndPostsSameLines()
    {
        using var fixture = new BillingFixture(1);
        fixture.Tariff.Rate = .50m;
        var reserve = new Fund { OrganizationId = fixture.Zev.OrganizationId, ZevId = fixture.Zev.Id,
            Kind = FundKind.Reserve, Name = "Reserve" };
        var reserveTariff = new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = reserve.Id, Fund = reserve, Code = "RESERVE",
            Name = "Rezervni fond", Version = 1, Basis = TariffBasis.SquareMeter, Rate = .30m,
            ValidFrom = new DateOnly(2026, 1, 1) };
        var cleaning = new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Tariff.FundId, Code = "CLEAN",
            Name = "Čišćenje", Version = 1, Basis = TariffBasis.FixedUnit, Rate = 5m,
            ValidFrom = new DateOnly(2026, 1, 1) };
        fixture.Units[0].AreaSquareMeters = 60m;
        fixture.Database.AddRange(reserve, reserveTariff, cleaning);
        fixture.Database.SaveChanges();
        var period = new DateOnly(2026, 10, 1);
        var due = new DateOnly(2026, 10, 20);
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, due, null, CancellationToken.None);
        Assert.Equal(BillingFailure.None, preview.Failure);
        Assert.Equal(53m, preview.TotalAmount);
        Assert.Equal(53m, Assert.Single(preview.Units).Lines.Sum(line => line.Amount));
        Assert.Empty(fixture.Database.Charges);
        var posted = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, due,
            new BillingDraftRequest(null, null, preview.Fingerprint), CancellationToken.None);
        Assert.Equal(53m, posted.Run?.TotalAmount);
        Assert.Equal(3, fixture.Database.Charges.Count());
        Assert.Equal(53m, fixture.Database.Charges.Sum(charge => charge.Amount));
        Assert.True((await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, due,
            CancellationToken.None)).AlreadyExists);
    }

    [Fact]
    public async Task DraftManualAmountAndOptionalRemovalRequireExplicitSelection()
    {
        using var fixture = new BillingFixture(1);
        var manual = new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Tariff.FundId, Code = "DAMAGE",
            Name = "Sanacija", Version = 1, Basis = TariffBasis.ManualAmount, Rate = 0m,
            IsOptional = true, ValidFrom = new DateOnly(2026, 1, 1) };
        var oneTime = new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Tariff.FundId, Code = "ROOF",
            Name = "Krov", Version = 1, Basis = TariffBasis.OneTime, Rate = 20m,
            ValidFrom = new DateOnly(2026, 10, 1) };
        fixture.Database.AddRange(manual, oneTime);
        fixture.Database.SaveChanges();
        var period = new DateOnly(2026, 10, 1);
        var due = new DateOnly(2026, 10, 20);
        var adjustment = new BillingDraftAdjustment(fixture.Units[0].Id, manual.Id, true, 35m,
            "Popravka", "Odobrena sanacija");
        var draft = new BillingDraftRequest([fixture.Tariff.Id, oneTime.Id], [adjustment], null);
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, due, draft, CancellationToken.None);
        Assert.Equal(115m, preview.TotalAmount);
        var stale = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, due,
            draft with { ExpectedHash = "STALE" }, CancellationToken.None);
        Assert.Equal(BillingFailure.Conflict, stale.Failure);
        Assert.Empty(fixture.Database.Charges);
        var posted = await fixture.Engine.GenerateAsync(fixture.Zev.Id, period, due,
            draft with { ExpectedHash = preview.Fingerprint }, CancellationToken.None);
        Assert.Equal(115m, posted.Run?.TotalAmount);
        Assert.Equal(35m, Assert.Single(fixture.Database.Charges.Where(charge => charge.TariffVersionId == manual.Id)).Amount);
        Assert.True(Assert.Single(fixture.Database.Charges.Where(charge => charge.TariffVersionId == manual.Id)).IsManualOverride);
        Assert.Equal(BillingFailure.AlreadyPosted, (await fixture.Engine.PreviewAsync(fixture.Zev.Id,
            period, due, draft, CancellationToken.None)).Failure);
    }

    [Fact]
    public async Task PreviewExplainsMissingOwnerAndInvalidTariff()
    {
        using var fixture = new BillingFixture(1);
        var period = new DateOnly(2026, 10, 1);
        var due = new DateOnly(2026, 10, 30);
        fixture.Database.Ownerships.RemoveRange(fixture.Database.Ownerships);
        fixture.Database.SaveChanges();
        var noOwner = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, due, null, CancellationToken.None);
        Assert.Equal(BillingFailure.Conflict, noOwner.Failure);
        Assert.Contains("U-0 has no active owner", noOwner.Detail);
        Assert.Equal("PROPERTY_UNIT_NO_ACTIVE_OWNER", Assert.Single(noOwner.Issues).Code);

        var person = new Person { OrganizationId = fixture.Zev.OrganizationId, DisplayName = "Replacement" };
        fixture.Database.Add(person);
        fixture.Database.Ownerships.Add(new Ownership { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, PropertyUnitId = fixture.Units[0].Id, PersonId = person.Id,
            ValidFrom = period, Share = 1m });
        fixture.Database.SaveChanges();
        var invalid = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, due,
            new BillingDraftRequest([Guid.NewGuid()], null, null), CancellationToken.None);
        Assert.Equal(BillingFailure.Invalid, invalid.Failure);
        Assert.Equal("Selected tariff is not valid for selected period.", invalid.Detail);
    }

    [Fact]
    public async Task PreviewExplainsDuplicateActiveTariffVersions()
    {
        using var fixture = new BillingFixture(1);
        fixture.Database.TariffVersions.Add(new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Tariff.FundId, Code = fixture.Tariff.Code,
            Name = "Duplicate", Version = 2, Basis = TariffBasis.FixedUnit, Rate = 5m,
            ValidFrom = new DateOnly(2026, 10, 1) });
        fixture.Database.SaveChanges();
        var result = await fixture.Engine.PreviewAsync(fixture.Zev.Id, new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 30), null, CancellationToken.None);
        Assert.Equal(BillingFailure.Conflict, result.Failure);
        Assert.Equal("Multiple active tariff versions exist for code AREA.", result.Detail);
        Assert.Equal("MULTIPLE_ACTIVE_TARIFF_VERSIONS", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task PreflightCollectsAllMissingOwnersAndTariffProblems()
    {
        using var fixture = new BillingFixture(3);
        var missingUnits = fixture.Units.Skip(1).Select(item => item.Id).ToHashSet();
        fixture.Database.Ownerships.RemoveRange(fixture.Database.Ownerships
            .Where(item => missingUnits.Contains(item.PropertyUnitId)));
        var inactiveFund = new Fund { OrganizationId = fixture.Zev.OrganizationId, ZevId = fixture.Zev.Id,
            Kind = FundKind.Reserve, Name = "Inactive", IsActive = false };
        var inactiveFundTariff = new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = inactiveFund.Id, Fund = inactiveFund, Code = "INACTIVE",
            Name = "Inactive fund", Version = 1, Basis = TariffBasis.FixedUnit, Rate = 3m,
            ValidFrom = new DateOnly(2026, 10, 1) };
        var futureTariff = new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Tariff.FundId, Code = "FUTURE",
            Name = "Future", Version = 1, Basis = TariffBasis.FixedUnit, Rate = 4m,
            ValidFrom = new DateOnly(2026, 11, 1) };
        fixture.Database.AddRange(inactiveFund, inactiveFundTariff, futureTariff);
        fixture.Database.SaveChanges();
        var draft = new BillingDraftRequest([fixture.Tariff.Id, inactiveFundTariff.Id, futureTariff.Id], null, null);
        var period = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 10, 30);

        var preflight = await fixture.Engine.PreflightAsync(fixture.Zev.Id, period, dueDate, draft,
            CancellationToken.None);
        Assert.False(preflight.CanPreview);
        Assert.Equal(2, preflight.Issues.Count(item => item.Code == "PROPERTY_UNIT_NO_ACTIVE_OWNER"));
        Assert.Contains(preflight.Issues, item => item.Code == "TARIFF_FUND_INACTIVE"
            && item.TariffCode == "INACTIVE");
        Assert.Contains(preflight.Issues, item => item.Code == "SELECTED_TARIFF_INVALID_PERIOD"
            && item.TariffCode == "FUTURE");
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, dueDate, draft,
            CancellationToken.None);
        Assert.Equal(4, preview.Issues.Count);
        Assert.Empty(fixture.Database.Charges);
    }

    [Fact]
    public async Task PreflightUsesNewOwnershipWithoutRestartOrCachedFailure()
    {
        using var fixture = new BillingFixture(1);
        var period = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 10, 30);
        fixture.Database.Ownerships.RemoveRange(fixture.Database.Ownerships);
        fixture.Database.SaveChanges();
        Assert.Contains((await fixture.Engine.PreflightAsync(fixture.Zev.Id, period, dueDate, null,
            CancellationToken.None)).Issues, issue => issue.Code == "PROPERTY_UNIT_NO_ACTIVE_OWNER");

        var person = new Person { OrganizationId = fixture.Zev.OrganizationId, DisplayName = "New owner" };
        fixture.Database.Add(person);
        fixture.Database.Ownerships.Add(new Ownership { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, PropertyUnitId = fixture.Units[0].Id, PersonId = person.Id,
            ValidFrom = period, Share = 1m });
        fixture.Database.SaveChanges();

        Assert.True((await fixture.Engine.PreflightAsync(fixture.Zev.Id, period, dueDate, null,
            CancellationToken.None)).CanPreview);
        Assert.Equal(BillingFailure.None, (await fixture.Engine.PreviewAsync(fixture.Zev.Id,
            period, dueDate, null, CancellationToken.None)).Failure);
    }

    [Fact]
    public async Task DifferentCodesWithSameNameAndRateCanPreviewWhileOptionalIsNotDefault()
    {
        using var fixture = new BillingFixture(1);
        var optional = new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Tariff.FundId, Code = "UAT-AREA",
            Name = fixture.Tariff.Name, Version = 1, Basis = fixture.Tariff.Basis,
            Rate = fixture.Tariff.Rate, IsOptional = true, ValidFrom = new DateOnly(2026, 1, 1) };
        fixture.Database.TariffVersions.Add(optional);
        fixture.Database.SaveChanges();
        var period = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 10, 30);

        var defaultPreview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, dueDate, null,
            CancellationToken.None);
        Assert.Equal(BillingFailure.None, defaultPreview.Failure);
        Assert.Equal(60m, defaultPreview.TotalAmount);
        Assert.Equal(fixture.Tariff.Id, Assert.Single(Assert.Single(defaultPreview.Units).Lines).TariffVersionId);

        var both = await fixture.Engine.PreviewAsync(fixture.Zev.Id, period, dueDate,
            new BillingDraftRequest([fixture.Tariff.Id, optional.Id], null, null), CancellationToken.None);
        Assert.Equal(BillingFailure.None, both.Failure);
        Assert.Equal(120m, both.TotalAmount);
        Assert.Equal(2, Assert.Single(both.Units).Lines.Count);
    }

    [Fact]
    public async Task SelectedUnitPreviewIgnoresOtherUnitsButPostingStillRequiresAllOwners()
    {
        using var fixture = new BillingFixture(2);
        var secondUnit = fixture.Units[1];
        fixture.Database.Ownerships.RemoveRange(fixture.Database.Ownerships
            .Where(item => item.PropertyUnitId == secondUnit.Id));
        fixture.Database.TariffVersions.Add(new TariffVersion { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Tariff.FundId, Code = "OTHER",
            Name = "Other", Version = 1, Basis = TariffBasis.FixedUnit, Rate = 1m,
            ValidFrom = new DateOnly(2026, 10, 1) });
        fixture.Database.SaveChanges();
        var period = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 10, 30);
        var draft = new BillingDraftRequest([fixture.Tariff.Id], null, null);

        var preview = await fixture.Engine.PreviewSelectedUnitsAsync(fixture.Zev.Id, period, dueDate,
            draft, [fixture.Units[0].Id], CancellationToken.None);
        Assert.Equal(BillingFailure.None, preview.Failure);
        Assert.Equal(60m, preview.TotalAmount);
        Assert.Single(preview.Units);
        Assert.Equal(BillingFailure.Conflict, (await fixture.Engine.PreviewAsync(fixture.Zev.Id,
            period, dueDate, draft, CancellationToken.None)).Failure);
        Assert.Equal(BillingFailure.Conflict, (await fixture.Engine.GenerateAsync(fixture.Zev.Id,
            period, dueDate, draft, CancellationToken.None)).Failure);
        Assert.Empty(fixture.Database.BillingRuns);
    }

    private sealed class BillingFixture : IDisposable
    {
        public ZevDbContext Database { get; }
        public MonthlyBillingEngine Engine { get; }
        public Zev.Api.Domain.Zev Zev { get; }
        public TariffVersion Tariff { get; }
        public List<PropertyUnit> Units { get; } = [];

        public BillingFixture(int unitCount, Guid? actorId = null)
        {
            var options = new DbContextOptionsBuilder<ZevDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
            var context = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            if (actorId is Guid actor) context.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", actor.ToString())]));
            Database = new ZevDbContext(options, context);
            var organization = new Organization { Code = "TEST", Name = "Test" };
            Zev = new Zev.Api.Domain.Zev { OrganizationId = organization.Id, Organization = organization,
                Code = "ZEV", Name = "ZEV" };
            var building = new Building { OrganizationId = organization.Id, ZevId = Zev.Id, Zev = Zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = Zev.Id,
                BuildingId = building.Id, Building = building, Code = "E", Name = "Entrance" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = Zev.Id,
                Kind = FundKind.Regular, Name = "Regular" };
            Tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = Zev.Id,
                FundId = fund.Id, Fund = fund, Code = "AREA", Name = "Area", Version = 1,
                Basis = TariffBasis.SquareMeter, Rate = 2m, ValidFrom = new DateOnly(2026, 1, 1) };
            Database.AddRange(organization, Zev, building, entrance, fund, Tariff);
            for (var index = 0; index < unitCount; index++)
            {
                var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = Zev.Id,
                    BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance,
                    Code = $"U-{index}", AreaSquareMeters = 30m };
                var person = new Person { OrganizationId = organization.Id, Organization = organization,
                    Kind = PersonKind.Individual, DisplayName = $"Owner {index}" };
                Database.AddRange(unit, person);
                Units.Add(unit);
                Database.Ownerships.Add(new Ownership { OrganizationId = organization.Id, ZevId = Zev.Id,
                    PropertyUnitId = unit.Id, PropertyUnit = unit, PersonId = person.Id, Person = person,
                    ValidFrom = new DateOnly(2025, 1, 1), Share = 1m });
            }
            Database.SaveChanges();
            Engine = new MonthlyBillingEngine(Database);
        }

        public void Dispose() => Database.Dispose();
    }
}
