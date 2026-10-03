using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Tests;

public sealed class TenantApiTests
{
    [Fact]
    public async Task OwnerBalanceEndpointsShareCurrencyAsOfAndPagination()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost") });
          Guid zevId;
          Guid personId;
          Guid userId;
          Guid invoiceUnitId;
          Guid eurInvoiceUnitId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "PARITY", Name = "Parity" };
            var zev = new ZevEntity { Organization = organization, Code = "PARITY", Name = "Parity" };
            var user = new AppUser { Email = "parity@example.invalid",
                NormalizedEmail = "PARITY@EXAMPLE.INVALID", IsSystemAdministrator = true };
            var person = new Person { Organization = organization, User = user,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "Parity owner" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
              var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                  BuildingId = building.Id, Entrance = entrance, Code = "U", AreaSquareMeters = 60m };
              var eurUnit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                  BuildingId = building.Id, Entrance = entrance, Code = "U-EUR", AreaSquareMeters = 40m };
            var bamFund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "BAM" };
            var eurFund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "EUR" };
            var bamCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = bamFund, Amount = 100m,
                Period = new DateOnly(2026, 8, 1), DueDate = new DateOnly(2026, 8, 31),
                Reference = "BAM-CHARGE",
                CreatedUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero) };
            var eurCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                  PropertyUnit = eurUnit, Person = person, Fund = eurFund, Amount = 50m,
                Period = new DateOnly(2026, 8, 1), DueDate = new DateOnly(2026, 8, 31),
                Reference = "EUR-CHARGE",
                CreatedUtc = new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero) };
            var bamPayment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                  PersonId = person.Id, FundId = bamFund.Id, Amount = 80m, CurrencyCode = "BAM",
                PaidOn = new DateOnly(2026, 8, 10), SourceType = PaymentSourceType.Manual,
                RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "Parity",
                CreatedUtc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero) };
            var eurPayment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                PersonId = person.Id, FundId = eurFund.Id, Amount = 50m, CurrencyCode = "EUR",
                PaidOn = new DateOnly(2026, 8, 10), SourceType = PaymentSourceType.Manual,
                RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "Parity",
                CreatedUtc = new DateTimeOffset(2026, 8, 10, 1, 0, 0, TimeSpan.Zero) };
            var bamAllocation = new PaymentAllocation { OrganizationId = organization.Id,
                  ZevId = zev.Id, Payment = bamPayment, Charge = bamCharge, Amount = 80m,
                CreatedUtc = new DateTimeOffset(2026, 8, 10, 2, 0, 0, TimeSpan.Zero) };
              database.AddRange(organization, zev, user, person, building, entrance, unit, eurUnit,
                bamFund, eurFund, bamCharge, eurCharge, bamPayment, eurPayment, bamAllocation,
                new Membership { User = user, Organization = organization, Zev = zev,
                    Role = MembershipRole.Owner },
                  new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                      PropertyUnit = unit, Person = person, ValidFrom = new DateOnly(2026, 1, 1), Share = 1m },
                  new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                      PropertyUnit = eurUnit, Person = person, ValidFrom = new DateOnly(2026, 1, 1), Share = 1m },
                new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id,
                    Fund = bamFund, AccountNumber = "PARITY-BAM", CurrencyCode = "BAM" },
                new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id,
                    Fund = eurFund, AccountNumber = "PARITY-EUR", CurrencyCode = "EUR" },
                new PaymentAllocation { OrganizationId = organization.Id, ZevId = zev.Id,
                    Payment = eurPayment, Charge = eurCharge, Amount = 50m,
                    CreatedUtc = new DateTimeOffset(2026, 8, 10, 3, 0, 0, TimeSpan.Zero) });
            await database.SaveChangesAsync();
            database.PaymentAllocations.Add(new PaymentAllocation { OrganizationId = organization.Id,
                ZevId = zev.Id, PaymentId = bamPayment.Id, ChargeId = bamCharge.Id,
                  Amount = -20m, ReversesAllocationId = bamAllocation.Id,
                ReversalReason = "Correction",
                CreatedUtc = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero) });
            await database.SaveChangesAsync();
            zevId = zev.Id; personId = person.Id; userId = user.Id;
        }
          client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
              factory.Services.GetRequiredService<TokenService>()
                  .CreateAccessToken(new AppUser { Id = userId }).AccessToken);
          using (var summary = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
              $"/api/zevs/{zevId}/reports/summary")))
          {
              Assert.True(summary.RootElement.GetProperty("mixedCurrency").GetBoolean());
              Assert.Equal(System.Text.Json.JsonValueKind.Null,
                  summary.RootElement.GetProperty("billed").ValueKind);
          }
          Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(
              $"/api/zevs/{zevId}/reports/annual/2026")).StatusCode);
          Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(
              $"/api/zevs/{zevId}/reports/annual/2026.xlsx")).StatusCode);
          async Task<System.Text.Json.JsonDocument[]> ReadAllAsync(string suffix)
        {
            var admin = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
                $"/api/zevs/{zevId}/payments/owner-card/{personId}{suffix}"));
            var reports = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
                $"/api/zevs/{zevId}/reports/owner-card/{personId}{suffix}"));
            var portal = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
                $"/api/me/finance/{zevId}{suffix}"));
            return [admin, reports, portal];
        }
          foreach (var (date, expectedOpen, expectedCredit) in new[]
              { ("2026-08-31", 20m, 0m), ("2026-09-06", 40m, 20m) })
        {
            var responses = await ReadAllAsync($"?to={date}&page=1&pageSize=3");
            try
            {
                foreach (var response in responses)
                {
                    var body = response.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? response.RootElement[0] : response.RootElement;
                    var balances = body.GetProperty("balances").EnumerateArray().ToArray();
                    Assert.Equal(2, balances.Length);
                    var bam = balances.Single(item => item.GetProperty("currencyCode").GetString() == "BAM");
                    var eur = balances.Single(item => item.GetProperty("currencyCode").GetString() == "EUR");
                      Assert.Equal((100m, 80m, expectedOpen, expectedCredit, 20m),
                        (bam.GetProperty("totalCharges").GetDecimal(),
                            bam.GetProperty("totalPayments").GetDecimal(),
                            bam.GetProperty("open").GetDecimal(), bam.GetProperty("credit").GetDecimal(),
                            bam.GetProperty("netBalance").GetDecimal()));
                    Assert.Equal((50m, 50m, 0m, 0m, 0m),
                        (eur.GetProperty("totalCharges").GetDecimal(),
                            eur.GetProperty("totalPayments").GetDecimal(),
                            eur.GetProperty("open").GetDecimal(), eur.GetProperty("credit").GetDecimal(),
                            eur.GetProperty("netBalance").GetDecimal()));
                    Assert.Equal(3, body.GetProperty("events").GetArrayLength());
                    if (body.TryGetProperty("balance", out var legacyBalance))
                        Assert.Equal(System.Text.Json.JsonValueKind.Null, legacyBalance.ValueKind);
                }
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var fundId = await database.Funds.Where(item => item.ZevId == zevId && item.Name == "BAM")
                .Select(item => item.Id).SingleAsync();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync();
              var unitId = await database.PropertyUnits.Where(item => item.ZevId == zevId && item.Code == "U")
                  .Select(item => item.Id).SingleAsync();
              invoiceUnitId = unitId;
              eurInvoiceUnitId = await database.PropertyUnits
                  .Where(item => item.ZevId == zevId && item.Code == "U-EUR")
                  .Select(item => item.Id).SingleAsync();
              var eurFundId = await database.Funds.Where(item => item.ZevId == zevId && item.Name == "EUR")
                  .Select(item => item.Id).SingleAsync();
              var originalCharges = await database.Charges.Where(item => item.ZevId == zevId).ToListAsync();
              var bamOriginal = originalCharges.Single(item => item.Amount == 100m);
              var eurOriginal = originalCharges.Single(item => item.Amount == 50m);
              var run = new BillingRun { OrganizationId = organizationId, ZevId = zevId,
                  Period = new DateOnly(2026, 8, 1), DueDate = new DateOnly(2026, 8, 31),
                  ChargeCount = 2, TotalAmount = 150m };
              Invoice OriginalInvoice(Charge charge, string number, int sequence, string currency,
                  InvoiceDocumentStatus status) => new() { OrganizationId = organizationId, ZevId = zevId,
                  BillingRunId = run.Id, PropertyUnitId = charge.PropertyUnitId, PersonId = personId,
                  InvoiceNumber = number, NumberYear = 2026, NumberSequence = sequence,
                  Period = run.Period, IssueDate = run.Period, DueDate = run.DueDate,
                  CurrentChargeTotal = charge.Amount, TotalAmount = charge.Amount,
                  TotalDueAtIssue = charge.Amount, Currency = currency, DocumentStatus = status,
                  PersonNameSnapshot = "Parity owner", ZevNameSnapshot = "Parity", UnitCodeSnapshot = "U",
                  CreatedUtc = new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero) };
              var bamInvoice = OriginalInvoice(bamOriginal, "PARITY-BAM-1", 1, "BAM",
                  InvoiceDocumentStatus.Corrected);
              var eurInvoice = OriginalInvoice(eurOriginal, "PARITY-EUR-2", 2, "EUR",
                  InvoiceDocumentStatus.Cancelled);
              InvoiceLine OriginalLine(Invoice invoice, Charge charge) => new()
              { OrganizationId = organizationId, ZevId = zevId, Invoice = invoice, Charge = charge,
                  FundId = charge.FundId, FundNameSnapshot = invoice.Currency,
                  DescriptionSnapshot = "Original assessment", CodeSnapshot = "ORIGINAL",
                  Quantity = 1m, UnitPrice = charge.Amount, AmountBeforeDiscount = charge.Amount,
                  FinalAmount = charge.Amount };
              FinancialDocument Amendment(Invoice invoice, FinancialDocumentType type, decimal amount,
                  string number, int sequence) => new()
              { OrganizationId = organizationId, ZevId = zevId, OriginalInvoice = invoice,
                  RequestId = Guid.NewGuid(), Type = type, Series = type == FinancialDocumentType.Storno
                      ? FinancialDocumentSeries.Storno : FinancialDocumentSeries.Correction,
                  DocumentNumber = number, NumberYear = 2026, NumberSequence = sequence,
                  IssueDate = new DateOnly(2026, 9, 20), Reason = "Parity regression",
                  SignedAmount = amount, Currency = invoice.Currency,
                  OriginalInvoiceNumberSnapshot = invoice.InvoiceNumber,
                  PersonNameSnapshot = "Parity owner", ZevNameSnapshot = "Parity", UnitCodeSnapshot = "U",
                  CreatedUtc = new DateTimeOffset(2026, 9, 20, 0, 0, sequence, TimeSpan.Zero) };
              var correction = Amendment(bamInvoice, FinancialDocumentType.Credit, -10m,
                  "PARITY-COR-1", 1);
              var storno = Amendment(eurInvoice, FinancialDocumentType.Storno, -50m,
                  "PARITY-STO-1", 1);
              Charge AmendmentCharge(FinancialDocument document, Guid fund, decimal amount,
                  string reference) => new() { OrganizationId = organizationId, ZevId = zevId,
                  PropertyUnitId = document.OriginalInvoice.PropertyUnitId, PersonId = personId, FundId = fund,
                  FinancialDocumentId = document.Id, Amount = amount,
                  Period = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 30),
                  Reference = reference,
                  CreatedUtc = new DateTimeOffset(2026, 9, 20, 1, 0, 0, TimeSpan.Zero) };
              var correctionCharge = AmendmentCharge(correction, fundId, -10m, "PARITY-COR-CHARGE");
              var stornoCharge = AmendmentCharge(storno, eurFundId, -50m, "PARITY-STO-CHARGE");
              FinancialDocumentLine AmendmentLine(FinancialDocument document, Charge charge) => new()
              { OrganizationId = organizationId, ZevId = zevId, Document = document, Charge = charge,
                  FundId = charge.FundId, FundNameSnapshot = document.Currency,
                  DescriptionSnapshot = document.Type.ToString(), SignedAmount = charge.Amount };
              database.AddRange(run, bamInvoice, eurInvoice,
                  OriginalLine(bamInvoice, bamOriginal), OriginalLine(eurInvoice, eurOriginal),
                  correction, storno, correctionCharge, stornoCharge,
                  AmendmentLine(correction, correctionCharge), AmendmentLine(storno, stornoCharge));
              var eurAllocation = await database.PaymentAllocations
                  .SingleAsync(item => item.ChargeId == eurOriginal.Id && item.Amount == 50m);
              database.PaymentAllocations.Add(new PaymentAllocation
              { OrganizationId = organizationId, ZevId = zevId, PaymentId = eurAllocation.PaymentId,
                  ChargeId = eurOriginal.Id, Amount = -50m, ReversesAllocationId = eurAllocation.Id,
                  FinancialDocumentId = storno.Id,
                  CreatedUtc = new DateTimeOffset(2026, 9, 20, 2, 0, 0, TimeSpan.Zero) });
              database.Charges.AddRange(Enumerable.Range(0, 1001).Select(index => new Charge
            {
                OrganizationId = organizationId,
                ZevId = zevId, PropertyUnitId = unitId, PersonId = personId, FundId = fundId,
                Amount = 1m, Period = new DateOnly(2026, 10, 1),
                Reference = $"BULK-{index}"
            }));
            await database.SaveChangesAsync();
        }
        var pages = await ReadAllAsync("?page=1&pageSize=20");
        try
        {
            foreach (var response in pages)
            {
                  var body = response.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                      ? response.RootElement[0] : response.RootElement;
                  var balances = body.GetProperty("balances").EnumerateArray().ToArray();
                  var bam = balances.Single(item => item.GetProperty("currencyCode").GetString() == "BAM");
                  var eur = balances.Single(item => item.GetProperty("currencyCode").GetString() == "EUR");
                  Assert.Equal((90m, 80m, 30m, 20m, 10m),
                      (bam.GetProperty("totalCharges").GetDecimal(),
                          bam.GetProperty("totalPayments").GetDecimal(), bam.GetProperty("open").GetDecimal(),
                          bam.GetProperty("credit").GetDecimal(), bam.GetProperty("netBalance").GetDecimal()));
                  Assert.Equal((0m, 50m, 0m, 50m, -50m),
                      (eur.GetProperty("totalCharges").GetDecimal(),
                          eur.GetProperty("totalPayments").GetDecimal(), eur.GetProperty("open").GetDecimal(),
                          eur.GetProperty("credit").GetDecimal(), eur.GetProperty("netBalance").GetDecimal()));
                  var unknown = balances.Single(item => item.GetProperty("currencyCode").GetString() == "UNKNOWN");
                  Assert.Equal(1001m, unknown.GetProperty("totalCharges").GetDecimal());
                  Assert.Equal(1001m, unknown.GetProperty("open").GetDecimal());
                  Assert.Equal(1011, body.GetProperty("totalCount").GetInt32());
                  Assert.Equal(20, body.GetProperty("events").GetArrayLength());
            }
        }
          finally { foreach (var response in pages) response.Dispose(); }
          var amendmentPages = await ReadAllAsync("?from=2026-09-20&page=1&pageSize=5");
          try
          {
              foreach (var response in amendmentPages)
              {
                  var body = response.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                      ? response.RootElement[0] : response.RootElement;
                  var kinds = body.GetProperty("events").EnumerateArray()
                      .Select(item => item.GetProperty("kind").GetString()).ToArray();
                  Assert.Contains("Correction", kinds);
                  Assert.Contains("Storno", kinds);
                  Assert.Contains("AllocationRelease", kinds);
              }
          }
          finally { foreach (var response in amendmentPages) response.Dispose(); }
          using (var unitLedger = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
              $"/api/zevs/{zevId}/payments/unit-ledger/{invoiceUnitId}?personId={personId}&page=1&pageSize=20")))
          {
              var root = unitLedger.RootElement;
              Assert.Equal(1005, root.GetProperty("totalCount").GetInt32());
              Assert.Equal(20, root.GetProperty("items").GetArrayLength());
              var bam = root.GetProperty("balancesByCurrency").EnumerateArray()
                  .Single(item => item.GetProperty("currencyCode").GetString() == "BAM");
              var unknown = root.GetProperty("balancesByCurrency").EnumerateArray()
                  .Single(item => item.GetProperty("currencyCode").GetString() == "UNKNOWN");
              Assert.Equal(30m, bam.GetProperty("open").GetDecimal());
              Assert.Equal(1001m, unknown.GetProperty("open").GetDecimal());
              Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("balance").ValueKind);
          }
          using (var secondPage = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
              $"/api/zevs/{zevId}/payments/unit-ledger/{invoiceUnitId}?personId={personId}&page=2&pageSize=20")))
          {
              Assert.Equal(1005, secondPage.RootElement.GetProperty("totalCount").GetInt32());
              Assert.Equal(20, secondPage.RootElement.GetProperty("items").GetArrayLength());
          }
          using (var eurUnit = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
              $"/api/zevs/{zevId}/payments/unit-ledger/{eurInvoiceUnitId}?personId={personId}")))
          {
              var balance = Assert.Single(eurUnit.RootElement.GetProperty("balancesByCurrency").EnumerateArray());
              Assert.Equal("EUR", balance.GetProperty("currencyCode").GetString());
              Assert.Equal(0m, balance.GetProperty("open").GetDecimal());
          }
          foreach (var (date, expectedOpen, expectedPaid) in new[]
              { ("2026-08-31", 20m, 80m), ("2026-09-06", 40m, 60m) })
          {
              var responses = await ReadAllAsync($"?to={date}&page=1&pageSize=20");
              try
              {
                  foreach (var response in responses)
                  {
                      var body = response.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                          ? response.RootElement[0] : response.RootElement;
                      var bam = body.GetProperty("balances").EnumerateArray()
                          .Single(item => item.GetProperty("currencyCode").GetString() == "BAM");
                      Assert.Equal(expectedOpen, bam.GetProperty("open").GetDecimal());
                      if (body.TryGetProperty("invoices", out var invoices))
                      {
                          var invoice = invoices.EnumerateArray()
                              .Single(item => item.GetProperty("currency").GetString() == "BAM");
                          Assert.Equal(expectedOpen, invoice.GetProperty("open").GetDecimal());
                          Assert.Equal(expectedPaid, invoice.GetProperty("paid").GetDecimal());
                      }
                  }
              }
              finally { foreach (var response in responses) response.Dispose(); }
              using var unit = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
                  $"/api/zevs/{zevId}/payments/unit-ledger/{invoiceUnitId}?personId={personId}&to={date}"));
              Assert.Equal(expectedOpen, unit.RootElement.GetProperty("balancesByCurrency")[0]
                  .GetProperty("open").GetDecimal());
          }
          foreach (var (currency, expectedCredit) in new[] { ("BAM", 20m), ("EUR", 50m) })
          {
              var unitId = currency == "BAM" ? invoiceUnitId : eurInvoiceUnitId;
              using var invoice = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
                  $"/api/zevs/{zevId}/finance/invoices/{unitId}/{personId}?period=2026-08-01"));
              Assert.Equal(currency, invoice.RootElement.GetProperty("currency").GetString());
              Assert.Equal(expectedCredit, invoice.RootElement.GetProperty("credit").GetDecimal());
              Assert.Equal(currency == "BAM" ? 90m : 0m,
                  invoice.RootElement.GetProperty("effectiveTotal").GetDecimal());
              Assert.Equal(currency == "BAM" ? 60m : 0m,
                  invoice.RootElement.GetProperty("currentPaid").GetDecimal());
              Assert.Equal(currency == "BAM" ? 30m : 0m,
                  invoice.RootElement.GetProperty("totalOpen").GetDecimal());
          }
    }

    [Fact]
    public async Task AutoCreditDoesNotRewriteIssuedFiftyCurrencyUnitInvoicePdf()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost") });
        Guid userId;
        Guid zevId;
        Guid unitId;
        Guid personId;
        Guid paymentId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "AUTO-PDF", Name = "Auto PDF" };
            var zev = new ZevEntity { Organization = organization, Code = "AUTO-PDF", Name = "Auto PDF" };
            var user = new AppUser { Email = "auto-pdf@example.invalid",
                NormalizedEmail = "AUTO-PDF@EXAMPLE.INVALID", IsSystemAdministrator = true };
            var building = new Building { OrganizationId = organization.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A1", AreaSquareMeters = 50m };
            var person = new Person { Organization = organization, DisplayName = "Owner" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var run = new BillingRun { OrganizationId = organization.Id, ZevId = zev.Id,
                Period = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 30),
                ChargeCount = 1, TotalAmount = 50m };
            var charge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = fund, BillingRun = run,
                Period = run.Period, DueDate = run.DueDate, Amount = 50m, Reference = "AUTO-PDF-CHARGE" };
            var invoice = new Invoice { OrganizationId = organization.Id, ZevId = zev.Id,
                BillingRunId = run.Id, PropertyUnitId = unit.Id, PersonId = person.Id,
                InvoiceNumber = "INV-2026-000001", NumberYear = 2026, NumberSequence = 1,
                Period = run.Period, IssueDate = run.Period, DueDate = run.DueDate,
                CurrentChargeTotal = 50m, TotalAmount = 50m, TotalDueAtIssue = 50m,
                ZevNameSnapshot = zev.Name, PersonNameSnapshot = person.DisplayName,
                UnitCodeSnapshot = unit.Code, UnitTypeSnapshot = "Apartment", Currency = "BAM" };
            var line = new InvoiceLine { OrganizationId = organization.Id, ZevId = zev.Id,
                Invoice = invoice, Charge = charge, FundId = fund.Id, FundNameSnapshot = fund.Name,
                CodeSnapshot = "FIX", DescriptionSnapshot = "Maintenance", UnitOfMeasureSnapshot = "kom",
                Quantity = 1m, UnitPrice = 50m, AmountBeforeDiscount = 50m, FinalAmount = 50m };
            var payment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                PersonId = person.Id, FundId = fund.Id, SourceType = PaymentSourceType.Manual,
                RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "Opening credit",
                Amount = 20m, CurrencyCode = "BAM", PaidOn = new DateOnly(2026, 9, 1) };
            database.AddRange(organization, zev, user, building, entrance, unit, person, fund, run,
                charge, invoice, line, payment,
                new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id,
                    Fund = fund, AccountNumber = "AUTO-PDF-ACCOUNT", CurrencyCode = "BAM" });
            await database.SaveChangesAsync();
            userId = user.Id; zevId = zev.Id; unitId = unit.Id; personId = person.Id;
            paymentId = payment.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        var path = $"/api/zevs/{zevId}/finance/invoices/{unitId}/{personId}";
        var originalPdf = await client.GetByteArrayAsync(path + ".pdf?period=2026-10-01");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/zevs/{zevId}/payments/{paymentId}/automatic-preview")).StatusCode);
        var applied = await client.PostAsJsonAsync($"/api/zevs/{zevId}/payments/{paymentId}/automatic",
            new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(originalPdf, await client.GetByteArrayAsync(path + ".pdf?period=2026-10-01"));
        using var detail = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            path + "?period=2026-10-01"));
        Assert.Equal(50m, detail.RootElement.GetProperty("currentTotal").GetDecimal());
        Assert.Equal(30m, detail.RootElement.GetProperty("totalOpen").GetDecimal());
        using var verification = factory.Services.CreateScope();
        var records = verification.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.Single(records.Payments);
        Assert.Equal(20m, Assert.Single(records.PaymentAllocations).Amount);
    }

    [Fact]
    public async Task SplitPaymentTimelineAndManualReversalShowInvoiceSpecificAmounts()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost") });
        Guid userId;
        Guid zevId;
        Guid firstInvoiceId;
        Guid secondInvoiceId;
        Guid firstAllocationId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "SPLIT", Name = "Split" };
            var zev = new ZevEntity { Organization = organization, Code = "SPLIT", Name = "Split" };
            var user = new AppUser { Email = "split-admin@example.invalid",
                NormalizedEmail = "SPLIT-ADMIN@EXAMPLE.INVALID", IsSystemAdministrator = true };
            var building = new Building { OrganizationId = organization.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var firstUnit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A", AreaSquareMeters = 60m };
            var secondUnit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "B", AreaSquareMeters = 40m };
            var person = new Person { Organization = organization, DisplayName = "Owner" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var run = new BillingRun { OrganizationId = organization.Id, ZevId = zev.Id,
                Period = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 30),
                ChargeCount = 2, TotalAmount = 100m };
            var firstCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = firstUnit, Person = person, Fund = fund, BillingRun = run,
                Period = run.Period, DueDate = run.DueDate, Amount = 60m, Reference = "SPLIT-A" };
            var secondCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = secondUnit, Person = person, Fund = fund, BillingRun = run,
                Period = run.Period, DueDate = run.DueDate, Amount = 40m, Reference = "SPLIT-B" };
            Invoice CreateInvoice(PropertyUnit unit, int sequence, decimal amount) => new()
            {
                OrganizationId = organization.Id, ZevId = zev.Id, BillingRunId = run.Id,
                PropertyUnitId = unit.Id, PersonId = person.Id,
                InvoiceNumber = $"INV-2026-{sequence:000000}", NumberYear = 2026,
                NumberSequence = sequence, Period = run.Period, IssueDate = run.Period,
                DueDate = run.DueDate, CurrentChargeTotal = amount, TotalAmount = amount,
                TotalDueAtIssue = amount, ZevNameSnapshot = zev.Name,
                PersonNameSnapshot = person.DisplayName, UnitCodeSnapshot = unit.Code,
                UnitTypeSnapshot = "Apartment", Currency = "BAM"
            };
            var firstInvoice = CreateInvoice(firstUnit, 1, 60m);
            var secondInvoice = CreateInvoice(secondUnit, 2, 40m);
            InvoiceLine CreateLine(Invoice invoice, Charge charge, decimal amount) => new()
            {
                OrganizationId = organization.Id, ZevId = zev.Id,
                Invoice = invoice, Charge = charge, FundId = fund.Id,
                FundNameSnapshot = fund.Name, CodeSnapshot = "FIX",
                DescriptionSnapshot = "Maintenance", UnitOfMeasureSnapshot = "kom",
                Quantity = 1m, UnitPrice = amount, AmountBeforeDiscount = amount,
                FinalAmount = amount
            };
            var payment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                PersonId = person.Id, FundId = fund.Id, SourceType = PaymentSourceType.Manual,
                RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "Split test",
                Amount = 100m, CurrencyCode = "BAM", PaidOn = new DateOnly(2026, 10, 10) };
            var firstAllocation = new PaymentAllocation { OrganizationId = organization.Id,
                ZevId = zev.Id, Payment = payment, Charge = firstCharge, Amount = 60m };
            var secondAllocation = new PaymentAllocation { OrganizationId = organization.Id,
                ZevId = zev.Id, Payment = payment, Charge = secondCharge, Amount = 40m };
            database.AddRange(organization, zev, user, building, entrance, firstUnit, secondUnit,
                person, fund, run, firstCharge, secondCharge, firstInvoice, secondInvoice,
                CreateLine(firstInvoice, firstCharge, 60m), CreateLine(secondInvoice, secondCharge, 40m),
                payment, firstAllocation, secondAllocation);
            await database.SaveChangesAsync();
            userId = user.Id; zevId = zev.Id; firstInvoiceId = firstInvoice.Id;
            secondInvoiceId = secondInvoice.Id; firstAllocationId = firstAllocation.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        async Task<System.Text.Json.JsonDocument> TimelineAsync(Guid invoiceId) =>
            System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
                $"/api/zevs/{zevId}/finance/invoices/{invoiceId}/timeline"));
        using (var first = await TimelineAsync(firstInvoiceId))
        using (var second = await TimelineAsync(secondInvoiceId))
        {
            var firstPayment = first.RootElement.EnumerateArray().Single(item =>
                item.GetProperty("type").GetString() == "Payment");
            var secondPayment = second.RootElement.EnumerateArray().Single(item =>
                item.GetProperty("type").GetString() == "Payment");
            Assert.Equal(100m, firstPayment.GetProperty("amount").GetDecimal());
            Assert.Equal(60m, firstPayment.GetProperty("appliedToInvoice").GetDecimal());
            Assert.Equal(100m, secondPayment.GetProperty("amount").GetDecimal());
            Assert.Equal(40m, secondPayment.GetProperty("appliedToInvoice").GetDecimal());
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/zevs/{zevId}/payments/allocations/{firstAllocationId}/reverse",
            new { reason = "Wrong unit" })).StatusCode);
        using var corrected = await TimelineAsync(firstInvoiceId);
        var reversal = corrected.RootElement.EnumerateArray().Single(item =>
            item.GetProperty("type").GetString() == "ManualAllocationReversal");
        Assert.Equal(60m, reversal.GetProperty("amount").GetDecimal());
        Assert.Equal("Wrong unit", reversal.GetProperty("reason").GetString());
        Assert.Equal("split-admin@example.invalid", reversal.GetProperty("actorName").GetString());
    }
    [Fact]
    public async Task ManagerPaymentOverridesEnforceActionsAndZevBoundary()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid managerId;
        Guid zevId;
        Guid otherZevId;
        Guid fundId;
        Guid accountId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "PAY-A", Name = "Payment A" };
            var zev = new ZevEntity { Organization = organization, Code = "PAY-A", Name = "Payment A" };
            var otherOrganization = new Organization { Code = "PAY-B", Name = "Payment B" };
            var otherZev = new ZevEntity { Organization = otherOrganization, Code = "PAY-B", Name = "Payment B" };
            var manager = new AppUser { Email = "e2e.manager@test.local", NormalizedEmail = "E2E.MANAGER@TEST.LOCAL" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var account = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, AccountNumber = "TEST-ACCOUNT", CurrencyCode = "BAM" };
            database.AddRange(organization, zev, otherOrganization, otherZev, manager, fund, account,
                new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager });
            foreach (var key in new[] { "BankAccounts.Create", "BankStatements.Import", "Payments.ReconcileManual",
                "Payments.Reverse", "Payments.CreateManual", "Payments.ReverseAllocation",
                "Payments.ChangePayer" })
                database.AccessOverrides.Add(new AccessOverride { UserId = manager.Id,
                    OrganizationId = organization.Id, ZevId = zev.Id, PermissionKey = key, Effect = AccessEffect.Deny,
                    ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), CreatedByUserId = manager.Id,
                    Reason = "UAT permissions" });
            await database.SaveChangesAsync();
            managerId = manager.Id; zevId = zev.Id; otherZevId = otherZev.Id;
            fundId = fund.Id; accountId = account.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = managerId }).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/zevs/{zevId}/payments/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/zevs/{zevId}/payments/accounts",
            new { fundId, accountNumber = "DENIED", currencyCode = "BAM" })).StatusCode);
        var statement = new { bankAccountId = accountId, fileName = "bank.csv", format = "CSV",
            contentBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("date,amount\n2026-09-01,1.00")) };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/zevs/{zevId}/payments/statements/preview",
            statement)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/zevs/{zevId}/payments/statements",
            statement)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            $"/api/zevs/{zevId}/payments/bank-transactions/{Guid.NewGuid()}/allocate",
            new { allocations = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/zevs/{zevId}/payments/manual",
            new { requestId = Guid.NewGuid(), personId = Guid.NewGuid(), fundId, paidOn = "2026-09-01",
                amount = 1m, currencyCode = "BAM", method = "Migration", reason = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/zevs/{zevId}/payments/{Guid.NewGuid()}/allocations",
            new { requestId = Guid.NewGuid(), allocations = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/zevs/{zevId}/payments/allocations/{Guid.NewGuid()}/reverse",
            new { reason = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/zevs/{zevId}/payments/{Guid.NewGuid()}/change-payer",
            new { personId = Guid.NewGuid(), reason = "Denied", allocations = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/zevs/{zevId}/payments/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/zevs/{otherZevId}/payments/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/zevs/{otherZevId}/payments/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            $"/api/zevs/{otherZevId}/access/users/{managerId}/permissions",
            new { reason = "Cross-ZEV attempt", changes = new[] { new { key = "Payments.View", effect = "Grant" } } })).StatusCode);
    }

    [Fact]
    public async Task MatrixSaveAppliesDenyThenResetWithoutChangingRoleDefault()
    {
        using var factory = new TestFactory();
        using var administrator = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var managerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid administratorId;
        Guid managerId;
        Guid zevId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "MATRIX", Name = "Matrix" };
            var zev = new ZevEntity { Organization = organization, Code = "MATRIX", Name = "Matrix" };
            var admin = new AppUser { Email = "matrix-admin@example.invalid", NormalizedEmail = "MATRIX-ADMIN@EXAMPLE.INVALID",
                IsSystemAdministrator = true };
            var manager = new AppUser { Email = "matrix-manager@example.invalid", NormalizedEmail = "MATRIX-MANAGER@EXAMPLE.INVALID" };
            database.AddRange(organization, zev, admin, manager,
                new Membership { User = manager, Organization = organization, Zev = zev, Role = MembershipRole.Manager });
            await database.SaveChangesAsync();
            administratorId = admin.Id; managerId = manager.Id; zevId = zev.Id;
        }
        var tokens = factory.Services.GetRequiredService<TokenService>();
        administrator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = administratorId }).AccessToken);
        managerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = managerId }).AccessToken);
        var path = $"/api/zevs/{zevId}/access/users/{managerId}/permissions";
        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync($"/api/zevs/{zevId}/payments/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await administrator.PutAsJsonAsync(path,
            new { reason = "UAT restriction", changes = new[] { new { key = "BankAccounts.View", effect = "Deny" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync($"/api/zevs/{zevId}/payments/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.PutAsJsonAsync(path,
            new { reason = "Self reset attempt", changes = new[] { new { key = "BankAccounts.View", effect = (string?)null } } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await administrator.PutAsJsonAsync(path,
            new { reason = "UAT reset", changes = new[] { new { key = "BankAccounts.View", effect = (string?)null } } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync($"/api/zevs/{zevId}/payments/accounts")).StatusCode);
        using var scopeAfter = factory.Services.CreateScope();
        var databaseAfter = scopeAfter.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.Contains(await databaseAfter.AccessOverrides.Where(item => item.UserId == managerId).ToListAsync(),
            item => item.Effect == AccessEffect.Deny && item.RevokedUtc is not null);
    }

    [Fact]
    public async Task AdministratorCanVersionAndDeactivateCustomBillingItem()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid userId;
        Guid zevId;
        Guid fundId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "BILL", Name = "Billing" };
            var zev = new ZevEntity { Organization = organization, Code = "BILL", Name = "Billing ZEV" };
            var user = new AppUser { Email = "billing-admin@example.invalid",
                NormalizedEmail = "BILLING-ADMIN@EXAMPLE.INVALID", PasswordHash = "test-only",
                IsSystemAdministrator = true };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id,
                Kind = FundKind.Regular, Name = "Regular" };
            database.AddRange(organization, zev, user, fund);
            await database.SaveChangesAsync();
            userId = user.Id; zevId = zev.Id; fundId = fund.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        var first = await client.PostAsJsonAsync($"/api/zevs/{zevId}/finance/tariffs", new
        {
            code = "CLEAN", name = "Čišćenje", description = "Mjesečno", basis = "FIXED",
            rate = 5m, fundId, validFrom = "2026-10-01", isOptional = true,
            showOnInvoice = true, displayOrder = 3
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await client.PostAsJsonAsync($"/api/zevs/{zevId}/finance/tariffs", new
        {
            code = "CLEAN", name = "Čišćenje", basis = "FIXED", rate = 6m,
            fundId, validFrom = "2026-11-01", displayOrder = 3
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var versions = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/finance/tariffs"));
        var items = versions.RootElement.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("2026-11-01", items[1].GetProperty("validTo").GetString());
        var latest = items[0];
        var status = await client.PatchAsJsonAsync($"/api/zevs/{zevId}/finance/tariffs/" +
            latest.GetProperty("id").GetString() + "/status", new
        {
            isActive = false, rowVersion = latest.GetProperty("rowVersion").GetString()
        });
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var updated = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/finance/tariffs"));
        Assert.False(updated.RootElement.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
        using var billingItems = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/finance/billing-items?period=2026-10-01"));
        var activeItem = Assert.Single(billingItems.RootElement.EnumerateArray());
        Assert.Equal("CLEAN", activeItem.GetProperty("code").GetString());
        Assert.Equal("Regular", activeItem.GetProperty("fundName").GetString());
        Assert.True(activeItem.GetProperty("isOptional").GetBoolean());
        var preflight = await client.PostAsJsonAsync($"/api/zevs/{zevId}/finance/billing-runs/preflight", new
        {
            period = "2026-10-01", dueDate = "2026-10-30",
            draft = new { tariffVersionIds = new[] { activeItem.GetProperty("id").GetGuid() } }
        });
        Assert.Equal(HttpStatusCode.OK, preflight.StatusCode);
        using var preflightJson = System.Text.Json.JsonDocument.Parse(await preflight.Content.ReadAsStringAsync());
        Assert.False(preflightJson.RootElement.GetProperty("canPreview").GetBoolean());
        Assert.Contains(preflightJson.RootElement.GetProperty("issues").EnumerateArray(),
            issue => issue.GetProperty("code").GetString() == "NO_ACTIVE_UNITS");
    }

    [Fact]
    public async Task InvoiceAndPdfReadThreePostedChargesAcrossTwoFunds()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid userId;
        Guid zevId;
        Guid unitId;
        Guid personId;
        Guid regularFundId;
        Guid reserveFundId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "INV", Name = "Invoice" };
            var zev = new ZevEntity { Organization = organization, Code = "INV", Name = "Invoice ZEV" };
            var user = new AppUser { Email = "invoice-admin@example.invalid",
                NormalizedEmail = "INVOICE-ADMIN@EXAMPLE.INVALID", PasswordHash = "test-only",
                IsSystemAdministrator = true };
            var building = new Building { OrganizationId = organization.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A-01", AreaSquareMeters = 60m };
            var person = new Person { Organization = organization, DisplayName = "Test Owner" };
            var regular = new Fund { OrganizationId = organization.Id, ZevId = zev.Id,
                Kind = FundKind.Regular, Name = "Redovni fond" };
            var reserve = new Fund { OrganizationId = organization.Id, ZevId = zev.Id,
                Kind = FundKind.Reserve, Name = "Rezervni fond" };
            var items = new[]
            {
                ("MAIN", "Održavanje", regular, 30m),
                ("RES", "Rezerva", reserve, 18m),
                ("CLEAN", "Čišćenje", regular, 5m)
            };
            database.AddRange(organization, zev, user, building, entrance, unit, person, regular, reserve,
                new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = regular,
                    AccountNumber = "REG-ACCOUNT", CurrencyCode = "BAM" },
                new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = reserve,
                    AccountNumber = "RES-ACCOUNT", CurrencyCode = "BAM" });
            foreach (var (code, name, fund, amount) in items)
            {
                var tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zev.Id,
                    Fund = fund, Code = code, Name = name, Version = 1,
                    ValidFrom = new DateOnly(2026, 1, 1) };
                database.Charges.Add(new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnit = unit, Person = person, Fund = fund, TariffVersion = tariff,
                    Period = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 20),
                    Amount = amount, Reference = $"ZEV-{Guid.NewGuid():N}" });
            }
            await database.SaveChangesAsync();
            userId = user.Id; zevId = zev.Id; unitId = unit.Id; personId = person.Id;
            regularFundId = regular.Id; reserveFundId = reserve.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        var path = $"/api/zevs/{zevId}/finance/invoices/{unitId}/{personId}";
        using var invoice = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            path + "?period=2026-10-01"));
        Assert.Equal(53m, invoice.RootElement.GetProperty("currentTotal").GetDecimal());
        Assert.Equal(53m, invoice.RootElement.GetProperty("totalOpen").GetDecimal());
        Assert.Equal(3, invoice.RootElement.GetProperty("lines").GetArrayLength());
        Assert.Equal("UNKNOWN", invoice.RootElement.GetProperty("currency").GetString());
        Assert.Empty(invoice.RootElement.GetProperty("accounts").EnumerateArray());
        Assert.Equal(53m, invoice.RootElement.GetProperty("lines").EnumerateArray()
            .Sum(item => item.GetProperty("amount").GetDecimal()));
        var pdf = await client.GetAsync(path + ".pdf?period=2026-10-01");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var content = System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync());
        Assert.Contains(Convert.ToHexString(System.Text.Encoding.BigEndianUnicode.GetBytes("53.00")), content);
        Assert.Contains(Convert.ToHexString(System.Text.Encoding.BigEndianUnicode.GetBytes("valuta nepoznata")), content);
        var regularSlip = await client.GetAsync(path +
            $"/payment-slip.pdf?period=2026-10-01&fundId={regularFundId}");
        Assert.Equal(HttpStatusCode.Conflict, regularSlip.StatusCode);
        var reserveSlip = await client.GetAsync(path +
            $"/payment-slip.pdf?period=2026-10-01&fundId={reserveFundId}");
        Assert.Equal(HttpStatusCode.Conflict, reserveSlip.StatusCode);
    }

    [Fact]
    public async Task PostedInvoicePdfRemainsStableAfterMasterChangesAndSlipUsesOutstandingAmount()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid administratorId;
        Guid zevId;
        Guid unitId;
        Guid personId;
        Guid fundId;
        Guid chargeId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "INV2", Name = "Invoice V2" };
            var zev = new ZevEntity { Organization = organization, Code = "INV2", Name = "Original ZEV" };
            var administrator = new AppUser { Email = "invoice2-admin@example.invalid",
                NormalizedEmail = "INVOICE2-ADMIN@EXAMPLE.INVALID", IsSystemAdministrator = true };
            var building = new Building { OrganizationId = organization.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A-01", AreaSquareMeters = 60m };
            var person = new Person { Organization = organization, DisplayName = "Original Owner" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, Code = "AREA", Name = "Original Tariff", Version = 1,
                Basis = TariffBasis.SquareMeter, Rate = .50m, ValidFrom = new DateOnly(2026, 1, 1) };
            database.AddRange(organization, zev, administrator, building, entrance, unit, person, fund, tariff,
                new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = fund,
                    AccountNumber = "ORIGINAL-ACCOUNT", CurrencyCode = "BAM" },
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnit = unit, Person = person, Share = 1m, ValidFrom = new DateOnly(2025, 1, 1) });
            await database.SaveChangesAsync();
            administratorId = administrator.Id; zevId = zev.Id; unitId = unit.Id; personId = person.Id; fundId = fund.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = administratorId }).AccessToken);
        var post = await client.PostAsJsonAsync($"/api/zevs/{zevId}/finance/billing-runs",
            new { period = "2026-10-01", dueDate = "2026-10-30" });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            Assert.Equal(administratorId, Assert.Single(database.Invoices).IssuedByUserId);
            Assert.Contains(database.AuditLogs, item => item.EntityType == nameof(Invoice)
                && item.Action == "Added");
        }
        var path = $"/api/zevs/{zevId}/finance/invoices/{unitId}/{personId}";
        using var detail = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(path + "?period=2026-10-01"));
        Assert.StartsWith("INV-2026-", detail.RootElement.GetProperty("invoiceNumber").GetString());
        Assert.Equal(30m, detail.RootElement.GetProperty("currentTotal").GetDecimal());
        Assert.Equal("Original Tariff", detail.RootElement.GetProperty("lines")[0].GetProperty("name").GetString());
        var originalPdf = await client.GetByteArrayAsync(path + ".pdf?period=2026-10-01");
        Guid successorUserId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var zev = await database.Zevs.SingleAsync(item => item.Id == zevId);
            var unit = await database.PropertyUnits.SingleAsync(item => item.Id == unitId);
            var person = await database.People.SingleAsync(item => item.Id == personId);
            var ownership = await database.Ownerships.SingleAsync(item => item.PropertyUnitId == unitId);
            ownership.ValidTo = new DateOnly(2026, 11, 1);
            var successor = new AppUser { Email = "successor@example.invalid",
                NormalizedEmail = "SUCCESSOR@EXAMPLE.INVALID", PasswordHash = "test-only" };
            var successorPerson = new Person { OrganizationId = zev.OrganizationId, User = successor,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "Successor" };
            database.AddRange(successor, successorPerson,
                new Membership { User = successor, OrganizationId = zev.OrganizationId,
                    ZevId = zevId, Role = MembershipRole.Owner },
                new Ownership { OrganizationId = zev.OrganizationId, ZevId = zevId,
                    PropertyUnitId = unitId, Person = successorPerson,
                    ValidFrom = new DateOnly(2026, 11, 1), Share = 1m });
            successorUserId = successor.Id;
            var tariff = await database.TariffVersions.SingleAsync();
            zev.Name = "Changed ZEV"; unit.Code = "Changed unit"; person.DisplayName = "Changed Owner";
            tariff.Name = "Changed Tariff"; tariff.Rate = 9m;
            var charge = await database.Charges.SingleAsync();
            chargeId = charge.Id;
            var payment = new Payment { OrganizationId = zev.OrganizationId, ZevId = zevId,
                FundId = fundId, PersonId = personId, CurrencyCode = "BAM", Amount = 10m,
                PaidOn = new DateOnly(2026, 10, 15), BankTransactionId = Guid.NewGuid() };
            database.Payments.Add(payment);
            database.PaymentAllocations.Add(new PaymentAllocation { OrganizationId = zev.OrganizationId,
                ZevId = zevId, PaymentId = payment.Id, ChargeId = charge.Id, Amount = 10m });
            await database.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = successorUserId }).AccessToken);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync(path + "?period=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync(path + ".pdf?period=2026-10-01")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = administratorId }).AccessToken);
        var updatedPdf = await client.GetByteArrayAsync(path + ".pdf?period=2026-10-01");
        Assert.Equal(originalPdf, updatedPdf);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var invoice = Assert.Single(database.Invoices);
            Assert.Equal(personId, invoice.PersonId);
            Assert.Equal("Original Owner", invoice.PersonNameSnapshot);
            Assert.Equal("A-01", invoice.UnitCodeSnapshot);
        }
        using var updatedDetail = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(path + "?period=2026-10-01"));
        Assert.Equal(10m, updatedDetail.RootElement.GetProperty("currentPaid").GetDecimal());
        Assert.Equal(20m, updatedDetail.RootElement.GetProperty("totalOpen").GetDecimal());
        var slip = await client.GetAsync(path + $"/payment-slip.pdf?period=2026-10-01&fundId={fundId}");
        Assert.Equal(HttpStatusCode.OK, slip.StatusCode);
        Assert.Contains(Convert.ToHexString(System.Text.Encoding.BigEndianUnicode.GetBytes("20.00")),
            System.Text.Encoding.ASCII.GetString(await slip.Content.ReadAsByteArrayAsync()));
        var chargeSlip = await client.GetAsync($"/api/zevs/{zevId}/charges/{chargeId}/payment-slip.pdf");
        Assert.Equal(HttpStatusCode.OK, chargeSlip.StatusCode);
        Assert.Contains(Convert.ToHexString(System.Text.Encoding.BigEndianUnicode.GetBytes("20.00")),
            System.Text.Encoding.ASCII.GetString(await chargeSlip.Content.ReadAsByteArrayAsync()));
    }


    [Fact]
    public async Task MobileRefreshRotatesTokenAndRejectsReplay()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var user = new AppUser { Email = "mobile@example.invalid", NormalizedEmail = "MOBILE@EXAMPLE.INVALID" };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, "mobile-password-123");
            database.Users.Add(user);
            await database.SaveChangesAsync();
        }
        var login = await client.PostAsJsonAsync("/api/auth/mobile/login",
            new { email = "mobile@example.invalid", password = "mobile-password-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var firstToken = loginJson.RootElement.GetProperty("refreshToken").GetString();
        Assert.NotNull(firstToken);
        var refresh = await client.PostAsJsonAsync("/api/auth/mobile/refresh", new { refreshToken = firstToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        using var refreshJson = System.Text.Json.JsonDocument.Parse(await refresh.Content.ReadAsStringAsync());
        var nextToken = refreshJson.RootElement.GetProperty("refreshToken").GetString();
        Assert.NotEqual(firstToken, nextToken);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/mobile/refresh", new { refreshToken = firstToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/mobile/logout", new { refreshToken = nextToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/mobile/refresh", new { refreshToken = nextToken })).StatusCode);
    }

    [Fact]
    public async Task OwnerFinanceShowsOnlyLinkedPersonAndRejectsManagerReports()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid userId;
        Guid zevId;
        Guid ownChargeId;
        Guid otherChargeId;
        Guid ownUnitId;
        Guid otherUnitId;
        Guid ownPersonId;
        Guid otherPersonId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "FIN", Name = "Finance" };
            var zev = new ZevEntity { Organization = organization, Code = "FIN", Name = "Finance ZEV" };
            var user = new AppUser { Email = "finance-owner@example.invalid", NormalizedEmail = "FINANCE-OWNER@EXAMPLE.INVALID", PasswordHash = "test-only" };
            var firstPerson = new Person { Organization = organization, User = user,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "First" };
            var secondPerson = new Person { Organization = organization, DisplayName = "Second",
                Email = "private-second@example.invalid", Phone = "PRIVATE-PHONE-SECOND",
                TaxIdentifier = "PRIVATE-TAX-SECOND" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var account = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, AccountNumber = "TEST-ACCOUNT", CurrencyCode = "BAM" };
            var tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, Code = "FIX", Name = "Fixed", Version = 1,
                Rate = 10m, ValidFrom = new DateOnly(2026, 1, 1) };
            database.AddRange(organization, zev, user, firstPerson, secondPerson, building, entrance, fund, account, tariff);
            database.Memberships.Add(new Membership { User = user, Organization = organization, Zev = zev, Role = MembershipRole.Owner });
            var firstUnit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance,
                Code = "1", AreaSquareMeters = 40m };
            var secondUnit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance,
                Code = "2", AreaSquareMeters = 50m };
            database.AddRange(firstUnit, secondUnit);
            database.Ownerships.AddRange(new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = firstUnit, Person = firstPerson, ValidFrom = new DateOnly(2025, 1, 1), Share = 1m },
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnit = secondUnit, Person = secondPerson, ValidFrom = new DateOnly(2025, 1, 1), Share = 1m });
            var ownCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = firstUnit, Person = firstPerson, Fund = fund, TariffVersion = tariff,
                Period = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 20), Amount = 10m, Reference = "OWN" };
            var otherCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = secondUnit, Person = secondPerson, Fund = fund, TariffVersion = tariff,
                Period = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 20), Amount = 99m, Reference = "OTHER" };
            database.Charges.AddRange(ownCharge, otherCharge);
            await database.SaveChangesAsync();
            userId = user.Id; zevId = zev.Id; ownChargeId = ownCharge.Id; otherChargeId = otherCharge.Id;
            ownUnitId = firstUnit.Id; otherUnitId = secondUnit.Id;
            ownPersonId = firstPerson.Id; otherPersonId = secondPerson.Id;
        }
        var token = factory.Services.GetRequiredService<TokenService>()
            .CreateAccessToken(new AppUser { Id = userId }).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync($"/api/me/finance/{zevId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(ownChargeId.ToString(), body);
        Assert.DoesNotContain(otherChargeId.ToString(), body);
        Assert.DoesNotContain("private-second@example.invalid", body);
        Assert.DoesNotContain("PRIVATE-PHONE-SECOND", body);
        Assert.DoesNotContain("PRIVATE-TAX-SECOND", body);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/reports/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/registry/people")).StatusCode);
        var slip = await client.GetAsync($"/api/zevs/{zevId}/charges/{ownChargeId}/payment-slip.pdf");
        Assert.Equal(HttpStatusCode.Conflict, slip.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/charges/{otherChargeId}/payment-slip.pdf")).StatusCode);
        var invoice = await client.GetAsync($"/api/zevs/{zevId}/finance/invoices/{ownUnitId}/{ownPersonId}?period=2026-09-01");
        Assert.Equal(HttpStatusCode.OK, invoice.StatusCode);
        using (var invoiceJson = System.Text.Json.JsonDocument.Parse(await invoice.Content.ReadAsStringAsync()))
            Assert.Equal(10m, invoiceJson.RootElement.GetProperty("currentTotal").GetDecimal());
        var invoicePdf = await client.GetAsync($"/api/zevs/{zevId}/finance/invoices/{ownUnitId}/{ownPersonId}.pdf?period=2026-09-01");
        Assert.Equal(HttpStatusCode.OK, invoicePdf.StatusCode);
        Assert.Contains(Convert.ToHexString(System.Text.Encoding.BigEndianUnicode.GetBytes("10.00")),
            System.Text.Encoding.ASCII.GetString(await invoicePdf.Content.ReadAsByteArrayAsync()));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/finance/invoices/{otherUnitId}/{otherPersonId}?period=2026-09-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/zevs/{zevId}/payments/unit-ledger/{ownUnitId}?personId={ownPersonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/payments/unit-ledger/{otherUnitId}?personId={otherPersonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/zevs/{zevId}/files", new
            {
                fileName = "unsafe.exe", contentType = "application/pdf",
                contentBase64 = Convert.ToBase64String("%PDF-1.4\n"u8.ToArray())
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/zevs/{zevId}/files", new
            {
                fileName = "large.pdf", contentType = "application/pdf",
                contentBase64 = Convert.ToBase64String(new byte[5_000_001])
            })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Users.Single(item => item.Id == userId).IsSystemAdministrator = true;
            await database.SaveChangesAsync();
        }
        var summary = await client.GetAsync($"/api/zevs/{zevId}/reports/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/zevs/{zevId}/registry/people")).StatusCode);
        Assert.Contains("109", await summary.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.GetAsync($"/api/zevs/{zevId}/reports/annual/2026.pdf")).StatusCode);
        var sheet = await client.GetAsync($"/api/zevs/{zevId}/reports/open-items.xlsx");
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        Assert.StartsWith("PK", System.Text.Encoding.ASCII.GetString((await sheet.Content.ReadAsByteArrayAsync())[..2]));
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var exports = database.AuditLogs.Where(item => item.ZevId == zevId && item.Action == "Export").ToList();
            Assert.Single(exports);
            Assert.All(exports, item =>
            {
                Assert.Equal(userId, item.ActorUserId);
                Assert.False(string.IsNullOrWhiteSpace(item.CorrelationId));
            });
        }
    }

    [Fact]
    public async Task OwnerCannotReadAnotherZevOrOrganizationThroughApi()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid userId;
        Guid ownZevId;
        Guid otherZevId;
        Guid organizationId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var owner = new AppUser { Email = "owner@example.invalid", NormalizedEmail = "OWNER@EXAMPLE.INVALID", PasswordHash = "test-only" };
            var ownOrganization = new Organization { Code = "OWN", Name = "Own organization" };
            var otherOrganization = new Organization { Code = "OTHER", Name = "Other organization" };
            var ownZev = new ZevEntity { Organization = ownOrganization, Code = "OWN", Name = "Own ZEV" };
            var otherZev = new ZevEntity { Organization = otherOrganization, Code = "OTHER", Name = "Other ZEV" };
            var building = new Building { OrganizationId = ownOrganization.Id,
                Zev = ownZev, Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = ownOrganization.Id, ZevId = ownZev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = ownOrganization.Id, ZevId = ownZev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A1", AreaSquareMeters = 50m };
            var person = new Person { Organization = ownOrganization, User = owner,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "Owner" };
            var ownership = new Ownership { OrganizationId = ownOrganization.Id, ZevId = ownZev.Id,
                PropertyUnit = unit, Person = person, ValidFrom = new DateOnly(2025, 1, 1), Share = 1m };
            database.AddRange(owner, ownOrganization, otherOrganization, ownZev, otherZev,
                building, entrance, unit, person, ownership);
            await database.SaveChangesAsync();
            userId = owner.Id;
            ownZevId = ownZev.Id;
            otherZevId = otherZev.Id;
            organizationId = ownOrganization.Id;
        }

        var tokenService = factory.Services.GetRequiredService<TokenService>();
        var accessToken = tokenService.CreateAccessToken(new AppUser { Id = userId }).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/zevs/{ownZevId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/zevs/{otherZevId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/organizations/{organizationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/system/organizations", new { code = "BAD", name = "Denied" })).StatusCode);
    }

    [Fact]
    public async Task ManagerCannotExportOrPreviewAnotherZevsFinance()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid managerId;
        Guid ownZevId;
        Guid otherZevId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var ownOrganization = new Organization { Code = "MA", Name = "Manager A" };
            var otherOrganization = new Organization { Code = "MB", Name = "Manager B" };
            var ownZev = new ZevEntity { Organization = ownOrganization, Code = "MA", Name = "Manager A" };
            var otherZev = new ZevEntity { Organization = otherOrganization, Code = "MB", Name = "Manager B" };
            var manager = new AppUser { Email = "manager@example.invalid",
                NormalizedEmail = "MANAGER@EXAMPLE.INVALID", PasswordHash = "test-only" };
            database.AddRange(ownOrganization, otherOrganization, ownZev, otherZev, manager);
            database.Memberships.Add(new Membership { User = manager, Organization = ownOrganization,
                Zev = ownZev, Role = MembershipRole.Manager });
            await database.SaveChangesAsync();
            managerId = manager.Id; ownZevId = ownZev.Id; otherZevId = otherZev.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = managerId }).AccessToken);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/zevs/{ownZevId}/reports/open-items.xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{otherZevId}/reports/open-items.xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{otherZevId}/registry/units")).StatusCode);
        var preview = new { bankAccountId = Guid.NewGuid(), fileName = "bank.csv", format = "CSV",
            contentBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("date,amount\n2026-09-01,1.00")) };
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/zevs/{otherZevId}/payments/statements/preview", preview)).StatusCode);
    }

    [Fact]
    public async Task SystemAdministratorCanCreateRegistryChainAndUpdateOrganizations()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid adminId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var admin = new AppUser { Email = "admin-chain@example.invalid",
                NormalizedEmail = "ADMIN-CHAIN@EXAMPLE.INVALID", PasswordHash = "test-only",
                IsSystemAdministrator = true };
            database.Users.Add(admin);
            await database.SaveChangesAsync();
            adminId = admin.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = adminId }).AccessToken);
        var organizationResponse = await client.PostAsJsonAsync("/api/system/organizations",
            new { code = "CHAIN", name = "Original" });
        Assert.Equal(HttpStatusCode.Created, organizationResponse.StatusCode);
        var organizationId = (await organizationResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        var organization = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/system/organizations/{organizationId}");
        var updateOrganization = await client.PutAsJsonAsync($"/api/system/organizations/{organizationId}",
            new { code = "CHAIN", name = "Updated", rowVersion = organization.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.OK, updateOrganization.StatusCode);
        var zevResponse = await client.PostAsJsonAsync("/api/system/zevs",
            new { organizationId, code = "CHAIN-Z", name = "Original ZEV" });
        Assert.Equal(HttpStatusCode.Created, zevResponse.StatusCode);
        var zevId = (await zevResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        var zev = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/system/zevs/{zevId}");
        var updateZev = await client.PutAsJsonAsync($"/api/system/zevs/{zevId}", new {
            code = "CHAIN-Z", name = "Updated ZEV", rowVersion = zev.GetProperty("rowVersion").GetString(),
            addressLine = "Test", cityMunicipality = "Test", taxIdentifier = (string?)null,
            primaryBankAccount = (string?)null, reserveFundBankAccount = (string?)null });
        Assert.Equal(HttpStatusCode.OK, updateZev.StatusCode);
        var building = await client.PostAsJsonAsync("/api/system/buildings",
            new { zevId, code = "B", name = "Building", addressLine = "Test" });
        Assert.Equal(HttpStatusCode.Created, building.StatusCode);
        var buildingId = (await building.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        var entrance = await client.PostAsJsonAsync("/api/system/entrances",
            new { buildingId, code = "E", name = "Entrance" });
        Assert.Equal(HttpStatusCode.Created, entrance.StatusCode);
        var entranceId = (await entrance.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        var unit = await client.PostAsJsonAsync("/api/system/property-units", new {
            entranceId, code = "U1", type = "Apartment", floor = 1,
            areaSquareMeters = 50m, coefficient = 1m, registeredMembers = 2 });
        Assert.Equal(HttpStatusCode.Created, unit.StatusCode);
        var unitId = (await unit.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        var person = await client.PostAsJsonAsync("/api/system/people", new {
            organizationId, kind = "Individual", displayName = "Synthetic Owner", userId = (Guid?)null,
            taxIdentifier = (string?)null, addressLine = (string?)null,
            cityMunicipality = (string?)null, email = (string?)null, phone = (string?)null });
        Assert.Equal(HttpStatusCode.Created, person.StatusCode);
        var personId = (await person.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        var ownership = await client.PostAsJsonAsync("/api/system/ownerships", new {
            propertyUnitId = unitId, personId, validFrom = "2026-01-01", validTo = (string?)null, share = 1m });
        Assert.Equal(HttpStatusCode.Created, ownership.StatusCode);
        var history = await client.GetStringAsync($"/api/system/property-units/{unitId}/ownerships");
        Assert.Contains("Synthetic Owner", history);
        var zevDetails = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/system/zevs/{zevId}");
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/system/zevs/{zevId}/status",
            new { isActive = false, rowVersion = zevDetails.GetProperty("rowVersion").GetString() })).StatusCode);
        zevDetails = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/system/zevs/{zevId}");
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/system/zevs/{zevId}/status",
            new { isActive = true, rowVersion = zevDetails.GetProperty("rowVersion").GetString() })).StatusCode);
        var organizationDetails = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/system/organizations/{organizationId}");
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/system/organizations/{organizationId}/status",
            new { isActive = false, rowVersion = organizationDetails.GetProperty("rowVersion").GetString() })).StatusCode);
        organizationDetails = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/system/organizations/{organizationId}");
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/system/organizations/{organizationId}/status",
            new { isActive = true, rowVersion = organizationDetails.GetProperty("rowVersion").GetString() })).StatusCode);
    }

    [Fact]
    public async Task ManagerRegistryCrudIsScopedAndInactiveMembershipIsDenied()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid managerId;
        Guid ownZevId;
        Guid otherZevId;
        Guid otherBuildingId;
        Guid membershipId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "SCOPE", Name = "Scope" };
            var ownZev = new ZevEntity { Organization = organization, Code = "A", Name = "A" };
            var otherZev = new ZevEntity { Organization = organization, Code = "B", Name = "B" };
            var manager = new AppUser { Email = "scoped-manager@example.invalid",
                NormalizedEmail = "SCOPED-MANAGER@EXAMPLE.INVALID", PasswordHash = "test-only" };
            var membership = new Membership { User = manager, Organization = organization,
                Zev = ownZev, Role = MembershipRole.Manager };
            var otherBuilding = new Building { OrganizationId = organization.Id,
                Zev = otherZev, Code = "B", Name = "Other" };
            database.AddRange(organization, ownZev, otherZev, manager, membership, otherBuilding);
            await database.SaveChangesAsync();
            managerId = manager.Id; ownZevId = ownZev.Id; otherZevId = otherZev.Id;
            otherBuildingId = otherBuilding.Id; membershipId = membership.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = managerId }).AccessToken);
        var prefix = $"/api/zevs/{ownZevId}/registry";
        var building = await client.PostAsJsonAsync($"{prefix}/buildings", new { code = "A", name = "Own" });
        Assert.Equal(HttpStatusCode.Created, building.StatusCode);
        var buildingJson = await building.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var buildingId = buildingJson.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/zevs/{otherZevId}/registry/buildings",
                new { code = "X", name = "Forbidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"{prefix}/buildings/{otherBuildingId}",
                new { code = "B", name = "Wrong", rowVersion = "" })).StatusCode);
        var entrance = await client.PostAsJsonAsync($"{prefix}/entrances",
            new { buildingId, code = "E", name = "Own entrance" });
        Assert.Equal(HttpStatusCode.Created, entrance.StatusCode);
        var entranceId = (await entrance.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        var unit = await client.PostAsJsonAsync($"{prefix}/units", new {
            entranceId, code = "U", type = "Apartment", floor = 1,
            areaSquareMeters = 55m, coefficient = 1m, registeredMembers = 1 });
        Assert.Equal(HttpStatusCode.Created, unit.StatusCode);
        var unitId = (await unit.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        var person = await client.PostAsJsonAsync($"{prefix}/people", new {
            kind = "Individual", displayName = "Own person" });
        Assert.Equal(HttpStatusCode.Created, person.StatusCode);
        var personId = (await person.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync($"{prefix}/ownerships", new {
                propertyUnitId = unitId, personId, validFrom = "2026-01-01", share = 1m
            })).StatusCode);
        var updateBuilding = await client.PutAsJsonAsync($"{prefix}/buildings/{buildingId}", new {
            code = "A", name = "Updated building", rowVersion = buildingJson.GetProperty("rowVersion").GetString()
        });
        Assert.Equal(HttpStatusCode.OK, updateBuilding.StatusCode);
        var updatedBuilding = await updateBuilding.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"{prefix}/buildings/{buildingId}/status",
            new { isActive = false, rowVersion = updatedBuilding.GetProperty("rowVersion").GetString() })).StatusCode);
        var unitDetails = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"{prefix}/units?pageSize=1");
        var unitVersion = unitDetails.GetProperty("items")[0].GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{prefix}/units/{unitId}", new {
            code = "U", type = "Apartment", floor = 2, areaSquareMeters = 60m,
            coefficient = 1m, registeredMembers = 1, rowVersion = unitVersion
        })).StatusCode);
        var people = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"{prefix}/people?pageSize=1");
        var personVersion = people.GetProperty("items")[0].GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{prefix}/people/{personId}", new {
            kind = "Individual", displayName = "Updated person", rowVersion = personVersion
        })).StatusCode);
        var ownerships = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"{prefix}/units/{unitId}/ownerships");
        var ownershipId = ownerships[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"{prefix}/ownerships/{ownershipId}/close", new { validTo = "2026-09-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{prefix}/people")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{otherZevId}/registry/people")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Memberships.Single(item => item.Id == membershipId).IsActive = false;
            await database.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{prefix}/units")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"{prefix}/buildings", new { code = "C", name = "Denied" })).StatusCode);
    }

    [Fact]
    public async Task OwnerCannotSeeAnotherPersonsPrivateDocument()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid ownerId;
        Guid zevId;
        Guid otherDocumentId;
        Guid otherFileId;
        Guid otherUnitDocumentId;
        Guid representativeId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "DOC", Name = "Documents" };
            var zev = new ZevEntity { Organization = organization, Code = "DOC", Name = "Document ZEV" };
            var owner = new AppUser { Email = "first@example.invalid", NormalizedEmail = "FIRST@EXAMPLE.INVALID", PasswordHash = "test-only" };
            var other = new AppUser { Email = "second@example.invalid", NormalizedEmail = "SECOND@EXAMPLE.INVALID", PasswordHash = "test-only" };
            var representative = new AppUser { Email = "representative@example.invalid",
                NormalizedEmail = "REPRESENTATIVE@EXAMPLE.INVALID", PasswordHash = "test-only" };
            var firstPerson = new Person { Organization = organization, User = owner,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "First" };
            var secondPerson = new Person { Organization = organization, User = other,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "Second" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A1", AreaSquareMeters = 50m };
            var otherUnit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A2", AreaSquareMeters = 55m };
            var ownership = new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = firstPerson, ValidFrom = new DateOnly(2025, 1, 1), Share = 1m };
            database.AddRange(organization, zev, owner, other, representative, firstPerson, secondPerson,
                building, entrance, unit, otherUnit, ownership,
                new Membership { User = representative, Organization = organization, Zev = zev,
                    Role = MembershipRole.BuildingRepresentative },
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnit = otherUnit, Person = secondPerson,
                    ValidFrom = new DateOnly(2025, 1, 1), Share = 1m });
            foreach (var visibility in new[] { DocumentVisibility.ZevMembers, DocumentVisibility.PersonOnly,
                         DocumentVisibility.PersonOnly, DocumentVisibility.ManagersOnly,
                         DocumentVisibility.PropertyUnit, DocumentVisibility.PropertyUnit,
                         DocumentVisibility.Role })
            {
                var file = new StoredFile { OrganizationId = organization.Id, ZevId = zev.Id,
                    UploadedByUserId = owner.Id, OriginalFileName = "test.pdf", ContentType = "application/pdf",
                    Sha256 = new string('A', 64), Length = 10, StorageKey = Guid.NewGuid().ToString("N") + ".pdf" };
                database.StoredFiles.Add(file);
                var document = new ManagedDocument { OrganizationId = organization.Id, ZevId = zev.Id,
                    StoredFileId = file.Id, UploadedByUserId = owner.Id, Title = visibility == DocumentVisibility.ZevMembers
                        ? "Public" : visibility == DocumentVisibility.ManagersOnly ? "Managers"
                        : visibility == DocumentVisibility.PropertyUnit ? "Unit"
                        : visibility == DocumentVisibility.Role ? "Owner role" : "Private",
                    Type = ManagedDocumentType.Other, Visibility = visibility,
                    PersonId = visibility == DocumentVisibility.PersonOnly && database.ManagedDocuments.Local
                        .Any(item => item.Visibility == DocumentVisibility.PersonOnly) ? secondPerson.Id
                        : visibility == DocumentVisibility.PersonOnly ? firstPerson.Id : null,
                    PropertyUnitId = visibility == DocumentVisibility.PropertyUnit
                        && database.ManagedDocuments.Local.Any(item => item.Visibility == DocumentVisibility.PropertyUnit)
                        ? otherUnit.Id : visibility == DocumentVisibility.PropertyUnit ? unit.Id : null,
                    TargetRole = visibility == DocumentVisibility.Role ? MembershipRole.Owner : null };
                database.ManagedDocuments.Add(document);
            }
            await database.SaveChangesAsync();
            ownerId = owner.Id;
            representativeId = representative.Id;
            zevId = zev.Id;
            otherDocumentId = database.ManagedDocuments.Local.Single(item => item.PersonId == secondPerson.Id).Id;
            otherFileId = database.ManagedDocuments.Local.Single(item => item.PersonId == secondPerson.Id).StoredFileId;
            otherUnitDocumentId = database.ManagedDocuments.Local.Single(item => item.PropertyUnitId == otherUnit.Id).Id;
        }

        var token = factory.Services.GetRequiredService<TokenService>()
            .CreateAccessToken(new AppUser { Id = ownerId }).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync($"/api/zevs/{zevId}/community/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var parsed = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(4, parsed.RootElement.GetProperty("items").GetArrayLength());
        Assert.Contains("Public", body);
        Assert.Contains("Private", body);
        Assert.Contains("Owner role", body);
        Assert.DoesNotContain("Managers", body);
        using (var representativeClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost") }))
        {
            representativeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                factory.Services.GetRequiredService<TokenService>()
                    .CreateAccessToken(new AppUser { Id = representativeId }).AccessToken);
            using var representativeDocuments = System.Text.Json.JsonDocument.Parse(await representativeClient.GetStringAsync(
                $"/api/zevs/{zevId}/community/documents"));
            Assert.Single(representativeDocuments.RootElement.GetProperty("items").EnumerateArray());
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/community/documents/{otherDocumentId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/files/{otherFileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/community/documents/{otherUnitDocumentId}/download")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var ownership = database.Ownerships.Single(item => item.Person.UserId == ownerId);
            ownership.ValidTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            database.AccessOverrides.Add(new AccessOverride { UserId = ownerId,
                OrganizationId = ownership.OrganizationId, ZevId = zevId,
                PermissionKey = AccessPermissions.Read, Effect = AccessEffect.Grant,
                ValidFromUtc = DateTimeOffset.UtcNow.AddDays(-1), ValidToUtc = DateTimeOffset.UtcNow.AddDays(1),
                Reason = "Read-only test access", CreatedByUserId = ownerId });
            await database.SaveChangesAsync();
        }
        var formerOwnerDocuments = await client.GetAsync($"/api/zevs/{zevId}/community/documents");
        Assert.Equal(HttpStatusCode.OK, formerOwnerDocuments.StatusCode);
        using var formerOwnerJson = System.Text.Json.JsonDocument.Parse(
            await formerOwnerDocuments.Content.ReadAsStringAsync());
        Assert.Equal(1, formerOwnerJson.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/zevs/{zevId}/community/documents/{otherUnitDocumentId}/download")).StatusCode);
    }

    [Fact]
    public async Task HistoricalBalanceKeepsPriorOwnerAndIgnoresLaterAllocation()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid adminId;
        Guid zevId;
        Guid firstPersonId;
        Guid secondPersonId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "HISTORY", Name = "History" };
            var zev = new ZevEntity { Organization = organization, Code = "HISTORY", Name = "History" };
            var admin = new AppUser { Email = "history@example.invalid", NormalizedEmail = "HISTORY@EXAMPLE.INVALID",
                PasswordHash = "test-only", IsSystemAdministrator = true };
            var first = new Person { Organization = organization, DisplayName = "Prior owner" };
            var second = new Person { Organization = organization, DisplayName = "New owner" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "B" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "E" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "1", AreaSquareMeters = 40m };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, Code = "FIX", Name = "Fixed", Rate = 10m, Version = 1,
                ValidFrom = new DateOnly(2026, 1, 1) };
            var account = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, AccountNumber = "HISTORY", CurrencyCode = "BAM" };
            var bankTransaction = new BankTransaction { OrganizationId = organization.Id, ZevId = zev.Id,
                Import = new BankStatementImport { OrganizationId = organization.Id, ZevId = zev.Id,
                    BankAccount = account, OriginalFileName = "test.csv", ChecksumSha256 = new string('A', 64),
                    Format = "CSV", TransactionCount = 1 }, Sequence = 1, Amount = 4m,
                BookingDate = new DateOnly(2026, 10, 10) };
            var priorCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = first, Fund = fund, TariffVersion = tariff,
                Period = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 15),
                Amount = 10m, Reference = "PRIOR",
                CreatedUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) };
            var newCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = second, Fund = fund, TariffVersion = tariff,
                Period = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 15),
                Amount = 10m, Reference = "NEW",
                CreatedUtc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) };
            var payment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                FundId = fund.Id, PersonId = first.Id, BankTransaction = bankTransaction,
                Amount = 4m, PaidOn = new DateOnly(2026, 10, 10), CurrencyCode = "BAM",
                CreatedUtc = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero) };
            database.AddRange(organization, zev, admin, first, second, building, entrance, unit, fund, tariff,
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnit = unit, Person = first, ValidFrom = new DateOnly(2025, 1, 1),
                    ValidTo = new DateOnly(2026, 10, 1), Share = 1m },
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnit = unit, Person = second, ValidFrom = new DateOnly(2026, 10, 1), Share = 1m },
                account, bankTransaction, priorCharge, newCharge, payment,
                new PaymentAllocation { OrganizationId = organization.Id, ZevId = zev.Id,
                    Payment = payment, Charge = priorCharge, Amount = 4m,
                    CreatedUtc = new DateTimeOffset(2026, 10, 10, 1, 0, 0, TimeSpan.Zero) });
            await database.SaveChangesAsync();
            adminId = admin.Id; zevId = zev.Id; firstPersonId = first.Id; secondPersonId = second.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = adminId }).AccessToken);
        var september = await client.GetStringAsync($"/api/zevs/{zevId}/reports/owner-card/{firstPersonId}?to=2026-09-30");
        using var septemberJson = System.Text.Json.JsonDocument.Parse(september);
        Assert.Equal(10m, septemberJson.RootElement.GetProperty("balance").GetDecimal());
        Assert.Equal(10m, septemberJson.RootElement.GetProperty("balances")[0].GetProperty("open").GetDecimal());
        var october = await client.GetStringAsync($"/api/zevs/{zevId}/reports/owner-card/{firstPersonId}?to=2026-10-31");
        using var octoberJson = System.Text.Json.JsonDocument.Parse(october);
        Assert.Equal(6m, octoberJson.RootElement.GetProperty("balance").GetDecimal());
        var newOwner = await client.GetStringAsync($"/api/zevs/{zevId}/reports/owner-card/{secondPersonId}?to=2026-10-31");
        using var newOwnerJson = System.Text.Json.JsonDocument.Parse(newOwner);
        Assert.Equal(10m, newOwnerJson.RootElement.GetProperty("balance").GetDecimal());
        var openItems = await client.GetStringAsync($"/api/zevs/{zevId}/reports/open-items?asOf=2026-09-30");
        using var openJson = System.Text.Json.JsonDocument.Parse(openItems);
        Assert.Equal(10m, openJson.RootElement.GetProperty("items")[0].GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task OwnerAccessAppearsOnlyAfterAdministratorVerificationAndEndsOnRevocation()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid adminId;
        Guid ownerId;
        Guid zevId;
        Guid personId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "LINK", Name = "Link" };
            var zev = new ZevEntity { Organization = organization, Code = "LINK", Name = "Link ZEV" };
            var admin = new AppUser { Email = "link-admin@example.invalid", NormalizedEmail = "LINK-ADMIN@EXAMPLE.INVALID",
                IsSystemAdministrator = true };
            var owner = new AppUser { Email = "link-owner@example.invalid", NormalizedEmail = "LINK-OWNER@EXAMPLE.INVALID" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "B" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "E" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A1", AreaSquareMeters = 50m };
            var person = new Person { Organization = organization, User = owner, DisplayName = "Owner" };
            database.AddRange(organization, zev, admin, owner, building, entrance, unit, person,
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit,
                    Person = person, ValidFrom = new DateOnly(2025, 1, 1), Share = 1m });
            await database.SaveChangesAsync();
            adminId = admin.Id; ownerId = owner.Id; zevId = zev.Id; personId = person.Id;
        }
        var tokens = factory.Services.GetRequiredService<TokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = ownerId }).AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/zevs/{zevId}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = adminId }).AccessToken);
        using var pending = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"/api/system/people/{personId}"));
        var firstVersion = pending.RootElement.GetProperty("rowVersion").GetString();
        var verified = await client.PostAsJsonAsync($"/api/system/people/{personId}/user-link/status",
            new { status = "Verified", reason = "Identity checked", rowVersion = firstVersion });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var verifiedBody = System.Text.Json.JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
        var secondVersion = verifiedBody.RootElement.GetProperty("rowVersion").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = ownerId }).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/zevs/{zevId}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = adminId }).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/system/people/{personId}/user-link/status", new { status = "Revoked",
                reason = "Identity revoked", rowVersion = secondVersion })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = ownerId }).AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/zevs/{zevId}")).StatusCode);
        using var auditScope = factory.Services.CreateScope();
        Assert.Equal(2, auditScope.ServiceProvider.GetRequiredService<ZevDbContext>().AuditLogs.Count(item => item.Action == "UserLink"));
    }

    [Fact]
    public async Task ManualOverrideCannotCrossZevAndDenyWins()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid managerId;
        Guid targetId;
        Guid firstId;
        Guid secondId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var firstOrg = new Organization { Code = "FIRST", Name = "First" };
            var secondOrg = new Organization { Code = "SECOND", Name = "Second" };
            var first = new ZevEntity { Organization = firstOrg, Code = "FIRST", Name = "First" };
            var second = new ZevEntity { Organization = secondOrg, Code = "SECOND", Name = "Second" };
            var manager = new AppUser { Email = "manager@example.invalid", NormalizedEmail = "MANAGER@EXAMPLE.INVALID" };
            var target = new AppUser { Email = "target@example.invalid", NormalizedEmail = "TARGET@EXAMPLE.INVALID" };
            database.AddRange(firstOrg, secondOrg, first, second, manager, target,
                new Membership { User = manager, Organization = firstOrg, Zev = first, Role = MembershipRole.Manager });
            await database.SaveChangesAsync();
            managerId = manager.Id; targetId = target.Id; firstId = first.Id; secondId = second.Id;
        }
        var tokens = factory.Services.GetRequiredService<TokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = managerId }).AccessToken);
        var request = new { userId = targetId, permissionKey = "zev.read", effect = "Grant",
            validFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), validToUtc = DateTimeOffset.UtcNow.AddDays(1),
            reason = "Temporary UAT access" };
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            $"/api/zevs/{secondId}/access/overrides", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            $"/api/zevs/{firstId}/access/overrides", request)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = targetId }).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/zevs/{firstId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/zevs/{secondId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/zevs/{firstId}/reports/open-items?asOf=2026-09-30")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = managerId }).AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            $"/api/zevs/{firstId}/access/overrides", new { request.userId, request.permissionKey,
                effect = "Deny", request.validFromUtc, request.validToUtc, reason = "Restricted UAT access" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.CreateAccessToken(new AppUser { Id = targetId }).AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/zevs/{firstId}")).StatusCode);
    }

    [Fact]
    public async Task FormerOwnerGetsOnlyHistoricalSelfFinanceWhenEnabled()
    {
        using var factory = new TestFactory(historicalSelfAccess: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid ownerId;
        Guid zevId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "HISTOWNER", Name = "History" };
            var zev = new ZevEntity { Organization = organization, Code = "HISTOWNER", Name = "History" };
            var owner = new AppUser { Email = "former@example.invalid", NormalizedEmail = "FORMER@EXAMPLE.INVALID" };
            var person = new Person { Organization = organization, User = owner,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "Former" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "B" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "E" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A1", AreaSquareMeters = 50m };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, Code = "FIX", Name = "Fixed", Version = 1, ValidFrom = new DateOnly(2026, 1, 1) };
            database.AddRange(organization, zev, owner, person, building, entrance, unit, fund, tariff,
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit,
                    Person = person, ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 7, 1), Share = 1m },
                new Charge { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit,
                    Person = person, Fund = fund, TariffVersion = tariff, Period = new DateOnly(2026, 6, 1),
                    DueDate = new DateOnly(2026, 6, 20), Amount = 10m, Reference = "OLD" },
                new Charge { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit,
                    Person = person, Fund = fund, TariffVersion = tariff, Period = new DateOnly(2026, 8, 1),
                    DueDate = new DateOnly(2026, 8, 20), Amount = 20m, Reference = "NEW" });
            await database.SaveChangesAsync();
            ownerId = owner.Id; zevId = zev.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = ownerId }).AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/zevs/{zevId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/zevs/{zevId}/community/documents")).StatusCode);
        using var communities = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/api/me/zevs"));
        Assert.True(communities.RootElement.GetProperty("items")[0].GetProperty("historicalSelfAccess").GetBoolean());
        using var card = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"/api/me/finance/{zevId}"));
        Assert.Equal(10m, card.RootElement[0].GetProperty("totalCharged").GetDecimal());
        Assert.Single(card.RootElement[0].GetProperty("events").EnumerateArray());
    }

    [Fact]
    public async Task InvoiceLifecycleApiEnforcesPermissionsAndOwnerPdfPrivacy()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Guid zevId;
        Guid invoiceId;
        Guid correctionInvoiceId;
        Guid correctionLineId;
        Guid unitId;
        Guid ownerPersonId;
        Guid administratorId;
        Guid managerId;
        Guid ownerId;
        Guid outsiderId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "LIFECYCLE-API", Name = "Lifecycle API" };
            var zev = new ZevEntity { Organization = organization, Code = "LIFECYCLE-API", Name = "Lifecycle API" };
            var administrator = new AppUser { Email = "lifecycle-admin@example.invalid",
                NormalizedEmail = "LIFECYCLE-ADMIN@EXAMPLE.INVALID", IsSystemAdministrator = true };
            var manager = new AppUser { Email = "lifecycle-manager@example.invalid",
                NormalizedEmail = "LIFECYCLE-MANAGER@EXAMPLE.INVALID" };
            var owner = new AppUser { Email = "lifecycle-owner@example.invalid",
                NormalizedEmail = "LIFECYCLE-OWNER@EXAMPLE.INVALID" };
            var outsider = new AppUser { Email = "lifecycle-outsider@example.invalid",
                NormalizedEmail = "LIFECYCLE-OUTSIDER@EXAMPLE.INVALID" };
            var person = new Person { Organization = organization, User = owner,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "Original owner" };
            var administratorPerson = new Person { Organization = organization, User = administrator,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "Upravnik Test" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A1", AreaSquareMeters = 60m };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var run = new BillingRun { OrganizationId = organization.Id, ZevId = zev.Id,
                Period = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 30),
                ChargeCount = 2, TotalAmount = 100m };
            var charge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = fund, BillingRun = run,
                Period = run.Period, DueDate = run.DueDate, Amount = 60m, Reference = "ZEV-LIFECYCLE-API" };
            var secondCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = fund, BillingRun = run,
                Period = run.Period, DueDate = run.DueDate, Amount = 40m, Reference = "ZEV-LIFECYCLE-API-2" };
            var invoice = new Invoice { OrganizationId = organization.Id, ZevId = zev.Id,
                BillingRunId = run.Id, PropertyUnitId = unit.Id, PersonId = person.Id,
                InvoiceNumber = "INV-2026-000001", NumberYear = 2026, NumberSequence = 1,
                Period = run.Period, IssueDate = run.Period, DueDate = run.DueDate,
                CurrentChargeTotal = 100m, TotalAmount = 100m, TotalDueAtIssue = 100m,
                ZevNameSnapshot = zev.Name, PersonNameSnapshot = person.DisplayName,
                UnitCodeSnapshot = unit.Code, UnitTypeSnapshot = "Apartment" };
            var line = new InvoiceLine { OrganizationId = organization.Id, ZevId = zev.Id,
                Invoice = invoice, Charge = charge, FundId = fund.Id, FundNameSnapshot = fund.Name,
                CodeSnapshot = "FIX", DescriptionSnapshot = "Maintenance", UnitOfMeasureSnapshot = "kom",
                Quantity = 1m, UnitPrice = 60m, AmountBeforeDiscount = 60m, FinalAmount = 60m };
            var secondLine = new InvoiceLine { OrganizationId = organization.Id, ZevId = zev.Id,
                Invoice = invoice, Charge = secondCharge, FundId = fund.Id, FundNameSnapshot = fund.Name,
                CodeSnapshot = "SECOND", DescriptionSnapshot = "Garage", UnitOfMeasureSnapshot = "kom",
                Quantity = 1m, UnitPrice = 40m, AmountBeforeDiscount = 40m, FinalAmount = 40m };
            var bankAccount = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, AccountNumber = "LIFECYCLE-ACCOUNT", CurrencyCode = "BAM" };
            var bankTransaction = new BankTransaction { OrganizationId = organization.Id, ZevId = zev.Id,
                Import = new BankStatementImport { OrganizationId = organization.Id, ZevId = zev.Id,
                    BankAccount = bankAccount, OriginalFileName = "lifecycle.csv",
                    ChecksumSha256 = new string('B', 64), Format = "CSV", TransactionCount = 1 },
                Sequence = 1, Amount = 40m, BookingDate = new DateOnly(2026, 9, 10) };
            var payment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                FundId = fund.Id, PersonId = person.Id, BankTransaction = bankTransaction,
                Amount = 40m, PaidOn = bankTransaction.BookingDate, CurrencyCode = "BAM" };
            var correctionRun = new BillingRun { OrganizationId = organization.Id, ZevId = zev.Id,
                Period = new DateOnly(2026, 8, 1), DueDate = new DateOnly(2026, 8, 30),
                ChargeCount = 1, TotalAmount = 50m };
            var correctionCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = fund, BillingRun = correctionRun,
                Period = correctionRun.Period, DueDate = correctionRun.DueDate,
                Amount = 50m, Reference = "ZEV-LIFECYCLE-CORRECTION" };
            var correctionInvoice = new Invoice { OrganizationId = organization.Id, ZevId = zev.Id,
                BillingRunId = correctionRun.Id, PropertyUnitId = unit.Id, PersonId = person.Id,
                InvoiceNumber = "INV-2026-000002", NumberYear = 2026, NumberSequence = 2,
                Period = correctionRun.Period, IssueDate = correctionRun.Period, DueDate = correctionRun.DueDate,
                CurrentChargeTotal = 50m, TotalAmount = 50m, TotalDueAtIssue = 50m,
                ZevNameSnapshot = zev.Name, PersonNameSnapshot = person.DisplayName,
                UnitCodeSnapshot = unit.Code, UnitTypeSnapshot = "Apartment" };
            var correctionLine = new InvoiceLine { OrganizationId = organization.Id, ZevId = zev.Id,
                Invoice = correctionInvoice, Charge = correctionCharge, FundId = fund.Id, FundNameSnapshot = fund.Name,
                CodeSnapshot = "FIX", DescriptionSnapshot = "Maintenance", UnitOfMeasureSnapshot = "kom",
                Quantity = 1m, UnitPrice = 50m, AmountBeforeDiscount = 50m, FinalAmount = 50m };
            database.AddRange(organization, zev, administrator, manager, owner, outsider, person,
                administratorPerson,
                building, entrance, unit, fund, run, charge, secondCharge, invoice, line, secondLine,
                bankAccount, bankTransaction, payment,
                new PaymentAllocation { OrganizationId = organization.Id, ZevId = zev.Id,
                    Payment = payment, Charge = charge, Amount = 20m },
                new PaymentAllocation { OrganizationId = organization.Id, ZevId = zev.Id,
                    Payment = payment, Charge = secondCharge, Amount = 20m },
                correctionRun, correctionCharge, correctionInvoice, correctionLine,
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit,
                    Person = person, ValidFrom = new DateOnly(2026, 1, 1), Share = 1m },
                new Membership { User = manager, Organization = organization, Zev = zev,
                    Role = MembershipRole.Manager },
                new AccessOverride { UserId = manager.Id, OrganizationId = organization.Id,
                    ZevId = zev.Id, PermissionKey = "Billing.Reverse", Effect = AccessEffect.Deny,
                    ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), CreatedByUserId = administrator.Id,
                    Reason = "Test denied" },
                new AccessOverride { UserId = manager.Id, OrganizationId = organization.Id,
                    ZevId = zev.Id, PermissionKey = "Invoice.Correct", Effect = AccessEffect.Deny,
                    ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), CreatedByUserId = administrator.Id,
                    Reason = "Test denied" });
            await database.SaveChangesAsync();
            zevId = zev.Id; invoiceId = invoice.Id; correctionInvoiceId = correctionInvoice.Id;
            correctionLineId = correctionLine.Id; unitId = unit.Id; ownerPersonId = person.Id;
            administratorId = administrator.Id; managerId = manager.Id;
            ownerId = owner.Id; outsiderId = outsider.Id;
        }

        void Authenticate(Guid userId) => client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.Services.GetRequiredService<TokenService>()
                .CreateAccessToken(new AppUser { Id = userId }).AccessToken);
        var path = $"/api/zevs/{zevId}/finance/invoices/{invoiceId}";
        Authenticate(managerId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{path}/storno",
            new { requestId = Guid.NewGuid(), reason = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{path}/corrections",
            new { requestId = Guid.NewGuid(), type = "Credit", reason = "Test", lines = new object[0] })).StatusCode);
        Authenticate(administratorId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{path}/corrections",
            new { requestId = Guid.NewGuid(), type = "Credit", reason = "Test JSON enum",
                lines = new[] { new { originalInvoiceLineId = Guid.NewGuid(), amount = 1m } } })).StatusCode);
        var originalPdf = await client.GetByteArrayAsync(
            $"/api/zevs/{zevId}/finance/invoices/{unitId}/{ownerPersonId}.pdf?period=2026-09-01");
        var response = await client.PostAsJsonAsync($"{path}/storno",
            new { requestId = Guid.NewGuid(), reason = "Pogrešno zaduženje" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var posted = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = posted.RootElement.GetProperty("id").GetGuid();
        using (var stornoDocument = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/finance/documents/{documentId}")))
            Assert.Equal("Upravnik Test", stornoDocument.RootElement.GetProperty("actorName").GetString());
        using (var timeline = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"{path}/timeline")))
        {
            Assert.Equal("Upravnik Test", timeline.RootElement.EnumerateArray()
                .Single(item => item.GetProperty("type").GetString() == "Storno")
                .GetProperty("actorName").GetString());
            Assert.Single(timeline.RootElement.EnumerateArray(),
                item => item.GetProperty("type").GetString() == "Payment");
            Assert.Equal(40m, timeline.RootElement.EnumerateArray()
                .Single(item => item.GetProperty("type").GetString() == "Payment")
                .GetProperty("amount").GetDecimal());
            Assert.Equal(40m, timeline.RootElement.EnumerateArray()
                .Single(item => item.GetProperty("type").GetString() == "AllocationRelease")
                .GetProperty("amount").GetDecimal());
        }
        var afterPdf = await client.GetByteArrayAsync(
            $"/api/zevs/{zevId}/finance/invoices/{unitId}/{ownerPersonId}.pdf?period=2026-09-01");
        Assert.Equal(originalPdf, afterPdf);
        using (var listed = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/finance/invoices?period=2026-09-01")))
        {
            var listedInvoice = listed.RootElement.GetProperty("items")[0];
            Assert.Equal("Cancelled", listedInvoice.GetProperty("documentStatus").GetString());
            Assert.Equal(0m, listedInvoice.GetProperty("effectiveTotal").GetDecimal());
            Assert.Equal(0m, listedInvoice.GetProperty("open").GetDecimal());
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/zevs/{zevId}/finance/documents/{documentId}.pdf")).StatusCode);
        var correctionResponse = await client.PostAsJsonAsync(
            $"/api/zevs/{zevId}/finance/invoices/{correctionInvoiceId}/corrections",
            new { requestId = Guid.NewGuid(), type = "Credit", reason = "Umanjenje",
                lines = new[] { new { originalInvoiceLineId = correctionLineId, amount = 10m } } });
        Assert.Equal(HttpStatusCode.OK, correctionResponse.StatusCode);
        using var correctionPosted = System.Text.Json.JsonDocument.Parse(await correctionResponse.Content.ReadAsStringAsync());
        var correctionDocumentId = correctionPosted.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/zevs/{zevId}/finance/documents/{correctionDocumentId}.pdf")).StatusCode);
        using (var correctionDocument = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/finance/documents/{correctionDocumentId}")))
            Assert.Equal("Upravnik Test", correctionDocument.RootElement.GetProperty("actorName").GetString());
        using (var openItems = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/reports/open-items")))
        {
            Assert.Equal(1, openItems.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(40m, openItems.RootElement.GetProperty("items")[0]
                .GetProperty("outstanding").GetDecimal());
        }
        var beforeDocumentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        using (var beforeCorrection = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/reports/open-items?asOf={beforeDocumentDate:yyyy-MM-dd}")))
        {
            Assert.Equal(0, beforeCorrection.RootElement.GetProperty("totalCount").GetInt32());
        }
        using (var debtors = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/expenses/debtors")))
        {
            Assert.Equal(1, debtors.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(40m, debtors.RootElement.GetProperty("items")[0]
                .GetProperty("outstanding").GetDecimal());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var administrator = await database.Users.SingleAsync(item => item.Id == administratorId);
            administrator.IsActive = false;
            await database.SaveChangesAsync();
        }
        Authenticate(ownerId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{path}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{path}/timeline")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/zevs/{zevId}/finance/documents/{documentId}.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/zevs/{zevId}/finance/documents/{correctionDocumentId}.pdf")).StatusCode);
        using (var inactiveActor = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/finance/invoices/{invoiceId}/documents")))
            Assert.Equal("Upravnik Test", inactiveActor.RootElement.GetProperty("documents")[0]
                .GetProperty("actorName").GetString());
        using (var cancelledDetail = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/finance/invoices/{unitId}/{ownerPersonId}?period=2026-09-01")))
        {
            Assert.Equal(40m, cancelledDetail.RootElement.GetProperty("paidBeforeStorno").GetDecimal());
            Assert.Equal(40m, cancelledDetail.RootElement.GetProperty("stornoCredit").GetDecimal());
            Assert.Equal(40m, cancelledDetail.RootElement.GetProperty("credit").GetDecimal());
            Assert.Equal(0m, cancelledDetail.RootElement.GetProperty("totalOpen").GetDecimal());
        }
        using (var card = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"/api/me/finance/{zevId}")))
        {
            Assert.Equal(0m, card.RootElement[0].GetProperty("balance").GetDecimal());
            Assert.Equal(2, card.RootElement[0].GetProperty("documents").GetArrayLength());
            var invoices = card.RootElement[0].GetProperty("invoices").EnumerateArray().ToArray();
            var cancelled = invoices.Single(item => item.GetProperty("id").GetGuid() == invoiceId);
            Assert.Equal("Cancelled", cancelled.GetProperty("documentStatus").GetString());
            Assert.Equal(-100m, cancelled.GetProperty("correctionAmount").GetDecimal());
            Assert.Equal(40m, cancelled.GetProperty("paidBeforeStorno").GetDecimal());
            Assert.Equal(40m, cancelled.GetProperty("stornoCredit").GetDecimal());
            var corrected = invoices.Single(item => item.GetProperty("id").GetGuid() == correctionInvoiceId);
            Assert.Equal("Corrected", corrected.GetProperty("documentStatus").GetString());
            Assert.Equal(-10m, corrected.GetProperty("correctionAmount").GetDecimal());
            Assert.Equal(40m, corrected.GetProperty("effectiveAmount").GetDecimal());
        }
        using (var ledger = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
            $"/api/zevs/{zevId}/payments/unit-ledger/{unitId}?personId={ownerPersonId}")))
            Assert.Equal(40m, ledger.RootElement.GetProperty("balance").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{path}/storno",
            new { requestId = Guid.NewGuid(), reason = "Unauthorized" })).StatusCode);
        Authenticate(outsiderId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{path}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/zevs/{zevId}/finance/documents/{documentId}.pdf")).StatusCode);
    }

    [Fact]
    public async Task AnnualXlsxExportRequiresSafeCurrencyAndAuditsSuccessfulExport()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost") });
        Guid zevId;
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            database.Database.EnsureCreated();
            var organization = new Organization { Code = "ANNUAL", Name = "Annual" };
            var zev = new ZevEntity { Organization = organization, Code = "ANNUAL", Name = "Annual ZEV" };
            var user = new AppUser { Email = "annual@example.invalid",
                NormalizedEmail = "ANNUAL@EXAMPLE.INVALID", IsSystemAdministrator = true };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "U", AreaSquareMeters = 40m };
            var person = new Person { Organization = organization, DisplayName = "Owner" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var charge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = fund, Amount = 50m,
                Period = new DateOnly(2026, 1, 1), DueDate = new DateOnly(2026, 1, 31),
                Reference = "ANNUAL-CHARGE" };
            var payment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                FundId = fund.Id, PersonId = person.Id, Amount = 50m, CurrencyCode = "BAM",
                PaidOn = new DateOnly(2026, 1, 15), SourceType = PaymentSourceType.Manual,
                RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "Annual export test" };
            database.AddRange(organization, zev, user, building, entrance, unit, person, fund, charge, payment,
                new PaymentAllocation { OrganizationId = organization.Id, ZevId = zev.Id,
                    Payment = payment, Charge = charge, Amount = 50m });
            await database.SaveChangesAsync();
            zevId = zev.Id;
            userId = user.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<TokenService>().CreateAccessToken(new AppUser { Id = userId }).AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(
            $"/api/zevs/{zevId}/reports/annual/1999.xlsx")).StatusCode);
        var response = await client.GetAsync($"/api/zevs/{zevId}/reports/annual/2026.xlsx");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("PK", System.Text.Encoding.ASCII.GetString((await response.Content.ReadAsByteArrayAsync())[..2]));
        using var verifyScope = factory.Services.CreateScope();
        var verifyDatabase = verifyScope.ServiceProvider.GetRequiredService<ZevDbContext>();
        Assert.Single(verifyDatabase.AuditLogs.Where(item => item.ZevId == zevId
            && item.Action == "Export" && item.EntityType == "AnnualReportXlsx"));
    }

    private sealed class TestFactory(bool historicalSelfAccess = false) : WebApplicationFactory<Program>
    {
        private readonly string databaseName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=TestOnly;Trusted_Connection=True",
                ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-characters",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Privacy:HistoricalSelfAccess"] = historicalSelfAccess.ToString()
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ZevDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ZevDbContext>>();
                services.RemoveAll<ZevDbContext>();
                services.AddDbContext<ZevDbContext>(options => options.UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }
    }
}
