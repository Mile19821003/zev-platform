using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Payments;

namespace Zev.Api.Tests;

// Bank statement import and automatic reconciliation with BiH-style CSV/TXT exports and both reference formats.
public sealed class StatementReconciliationTests
{
    [Fact]
    public async Task ShortReferenceFromBiHCsvAllocatesOldestOpenChargesOfTheAccountFundFirst()
    {
        using var fixture = new Fixture();
        var reference = await new PaymentReferenceService(fixture.Database)
            .EnsureAsync(fixture.ZevId, fixture.UnitA, fixture.OwnerA, CancellationToken.None);
        var spaced = $"{reference[..4]} {reference[4..8]} {reference[8..]}";
        // Semicolon separated, Bosnian headers, decimal comma, UTF-8 with BOM.
        var csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(
            "Datum valute;Iznos;Poziv na broj;Uplatilac;Svrha doznake;Valuta\r\n" +
            $"10.10.2026;35,00;{spaced};Ana Anić;Održavanje oktobar;BAM\r\n")).ToArray();

        var result = await fixture.Engine.ImportAsync(fixture.ZevId, fixture.RegularAccountId, "izvod.csv", "CSV", csv,
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(1, result.MatchedCount);
        var payment = Assert.Single(fixture.Database.Payments);
        Assert.Equal(35m, payment.Amount);
        Assert.Equal(fixture.OwnerA, payment.PersonId);
        Assert.Equal("Ana Anić", Assert.Single(fixture.Database.BankTransactions).Counterparty);
        Assert.Equal(20m, fixture.Allocated(fixture.OldestRegular));   // oldest due in the fund, fully paid
        Assert.Equal(15m, fixture.Allocated(fixture.NewerRegular));    // then the next one, partially
        Assert.Equal(0m, fixture.Allocated(fixture.OlderReserve));     // older, but another fund
        Assert.Equal(0m, fixture.Allocated(fixture.OtherOwnerCharge)); // another person's account
    }

    [Fact]
    public async Task LegacyReferenceFromWindows1250TxtWithDebitCreditColumnsStillMatches()
    {
        using var fixture = new Fixture();
        var reference = LegacyPaymentReference.Format(fixture.ZevId, fixture.UnitA, fixture.OwnerA);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // Tab separated TXT, Windows-1250, separate Duguje/Potražuje columns, thousands separator.
        var txt = Encoding.GetEncoding(1250).GetBytes(
            "Datum\tOpis\tDuguje\tPotražuje\tPoziv na broj\tNalogodavac\r\n" +
            $"11.10.2026.\tUplata zaduženja\t\t1.060,00\t{reference}\tĐuro Šimić\r\n");

        var result = await fixture.Engine.ImportAsync(fixture.ZevId, fixture.RegularAccountId, "izvod.txt", "TXT", txt,
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(1, result.MatchedCount);
        var payment = Assert.Single(fixture.Database.Payments);
        Assert.Equal(1060m, payment.Amount);
        Assert.Equal("Đuro Šimić", Assert.Single(fixture.Database.BankTransactions).Counterparty);
        Assert.Equal(20m, fixture.Allocated(fixture.OldestRegular));
        Assert.Equal(30m, fixture.Allocated(fixture.NewerRegular));
        Assert.Equal(0m, fixture.Allocated(fixture.OlderReserve));
        Assert.Equal(1010m, payment.Amount - fixture.Database.PaymentAllocations.Sum(item => item.Amount)); // credit kept
    }

    [Fact]
    public async Task WrongCheckDigitAndForeignZevReferencesAreNotMatched()
    {
        using var fixture = new Fixture();
        var references = new PaymentReferenceService(fixture.Database);
        var valid = await references.EnsureAsync(fixture.ZevId, fixture.UnitA, fixture.OwnerA, CancellationToken.None);
        var wrongCheckDigit = valid[..^1] + (char)('0' + (valid[^1] - '0' + 1) % 10);
        var foreign = LegacyPaymentReference.Format(Guid.NewGuid(), fixture.UnitA, fixture.OwnerA);
        var csv = Encoding.UTF8.GetBytes("date,amount,reference\n" +
            $"2026-10-10,20.00,{wrongCheckDigit}\n2026-10-10,20.00,{foreign}\n2026-10-10,20.00,12345\n");

        var result = await fixture.Engine.ImportAsync(fixture.ZevId, fixture.RegularAccountId, "x.csv", "CSV", csv,
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(0, result.MatchedCount);
        Assert.Equal(3, fixture.Database.Payments.Count());
        Assert.Empty(fixture.Database.PaymentAllocations);
        Assert.All(fixture.Database.Payments, payment => Assert.Null(payment.PersonId));
    }

    [Fact]
    public async Task UnparseableRowIsReportedWithRowNumberAndValue()
    {
        using var fixture = new Fixture();
        var csv = Encoding.UTF8.GetBytes("Datum;Iznos;Poziv na broj\n10.10.2026;30,00;1\n11.10.2026;trideset;2\n");
        var preview = await fixture.Engine.PreviewAsync(fixture.ZevId, fixture.RegularAccountId, "x.csv", "CSV", csv,
            CancellationToken.None);
        Assert.Equal("Statement could not be parsed: Row 3: invalid amount 'trideset'.", preview.Error);
    }

    [Theory]
    [InlineData("30,00", 30.00)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("-45,10", -45.10)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1.234", 1234)]
    public void AmountsInLocalAndInternationalNotationAreParsed(string text, double expected)
    {
        Assert.Equal((decimal)expected, DelimitedStatementParser.ParseAmount(text));
    }

    [Fact]
    public void InvalidAmountTextIsRejected()
    {
        Assert.Null(DelimitedStatementParser.ParseAmount("trideset"));
        Assert.Null(DelimitedStatementParser.ParseAmount(""));
        Assert.Equal(0m, DelimitedStatementParser.ParseAmount("", allowEmpty: true));
    }

    private sealed class Fixture : IDisposable
    {
        public ZevDbContext Database { get; }
        public PaymentReconciliationEngine Engine { get; }
        public Guid ZevId { get; }
        public Guid RegularAccountId { get; }
        public Guid UnitA { get; } = Guid.NewGuid();
        public Guid OwnerA { get; }
        public Guid OldestRegular { get; }
        public Guid NewerRegular { get; }
        public Guid OlderReserve { get; }
        public Guid OtherOwnerCharge { get; }

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
            Database = new ZevDbContext(options, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var organization = new Organization { Code = "REC", Name = "Reconciliation" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization, Code = "REC", Name = "ZEV" };
            var regular = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Redovni" };
            var reserve = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Reserve, Name = "Rezervni" };
            var regularAccount = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = regular,
                AccountNumber = "1610000012345678", CurrencyCode = "BAM" };
            var reserveAccount = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = reserve,
                AccountNumber = "1610000087654321", CurrencyCode = "BAM" };
            var ownerA = new Person { OrganizationId = organization.Id, DisplayName = "Ana Anić" };
            var ownerB = new Person { OrganizationId = organization.Id, DisplayName = "Bojan Babić" };
            Charge Charge(Person person, Guid unit, Fund fund, decimal amount, DateOnly due) => new()
            {
                OrganizationId = organization.Id, ZevId = zev.Id, PersonId = person.Id, PropertyUnitId = unit,
                FundId = fund.Id, Amount = amount, Period = new DateOnly(due.Year, due.Month, 1), DueDate = due,
                Reference = $"ZEV-{Guid.NewGuid():N}"
            };
            var newer = Charge(ownerA, UnitA, regular, 30m, new DateOnly(2026, 9, 30));
            var oldest = Charge(ownerA, UnitA, regular, 20m, new DateOnly(2026, 8, 31));
            var olderReserve = Charge(ownerA, UnitA, reserve, 15m, new DateOnly(2026, 7, 31));
            var other = Charge(ownerB, Guid.NewGuid(), regular, 50m, new DateOnly(2026, 6, 30));
            Database.AddRange(organization, zev, regular, reserve, regularAccount, reserveAccount, ownerA, ownerB,
                newer, oldest, olderReserve, other);
            Database.SaveChanges();
            ZevId = zev.Id; RegularAccountId = regularAccount.Id; OwnerA = ownerA.Id;
            OldestRegular = oldest.Id; NewerRegular = newer.Id; OlderReserve = olderReserve.Id; OtherOwnerCharge = other.Id;
            Engine = new PaymentReconciliationEngine(Database,
                [new DelimitedStatementParser("CSV", ','), new DelimitedStatementParser("TXT", ';')]);
        }

        public decimal Allocated(Guid chargeId) =>
            Database.PaymentAllocations.Where(item => item.ChargeId == chargeId).Sum(item => item.Amount);

        public void Dispose() => Database.Dispose();
    }
}
