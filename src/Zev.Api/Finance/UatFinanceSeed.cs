using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Registry;
using ZevEntity = Zev.Api.Domain.Zev;

namespace Zev.Api.Finance;

public sealed record UatFinanceSeedResult(IReadOnlyDictionary<string, bool> Checks, string? Error,
    decimal? UnitUatA01, decimal? UnitUatA02, decimal? Total)
{
    public bool Success => Error is null && Checks.Values.All(value => value);
}

public sealed class UatFinanceSeed(ZevDbContext database, MonthlyBillingEngine billing,
    IPasswordHasher<AppUser> passwordHasher)
{
    private static readonly DateOnly Period = new(2026, 10, 1);
    private static readonly DateOnly DueDate = new(2026, 10, 30);
    private static readonly string[] CheckNames =
    [
        "Organization", "ZEV A/B", "Building", "Entrance", "Units", "Ownership", "Funds",
        "Billing items", "Billing preview"
    ];

    public async Task<UatFinanceSeedResult> RunAsync(bool enabled, string environmentName,
        string? managerEmail, string? managerPassword, CancellationToken cancellationToken)
    {
        var checks = CheckNames.ToDictionary(name => name, _ => false);
        if (!enabled || environmentName is not ("Development" or "UAT"))
            return new(checks, "UAT finance seed requires Development/UAT and Seed:UatFinanceEnabled=true.", null, null, null);

        decimal? unitA01 = null;
        decimal? unitA02 = null;
        decimal? total = null;
        var currentCheck = "Organization";
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var organization = await database.Organizations.SingleOrDefaultAsync(item => item.Code == "TEST", cancellationToken);
            if (organization is null)
            {
                organization = new Organization { Code = "TEST", Name = "Test upravnik" };
                database.Organizations.Add(organization);
            }
            Require(organization.Name == "Test upravnik" && organization.IsActive,
                "Organization TEST must be active and named Test upravnik.");
            await database.SaveChangesAsync(cancellationToken);
            checks[currentCheck] = true;

            currentCheck = "ZEV A/B";
            var zevA = await EnsureZevAsync(organization, "TEST1", "TEST ZEV A", cancellationToken);
            await EnsureZevAsync(organization, "TEST2", "TEST ZEV B", cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            checks[currentCheck] = true;

            currentCheck = "Building";
            var building = await database.Buildings.SingleOrDefaultAsync(item => item.ZevId == zevA.Id
                && item.Code == "ZGR-A", cancellationToken);
            if (building is null)
            {
                building = new Building { OrganizationId = organization.Id, ZevId = zevA.Id,
                    Code = "ZGR-A", Name = "Test zgrada A" };
                database.Buildings.Add(building);
            }
            Require(building.Name == "Test zgrada A" && building.IsActive &&
                building.OrganizationId == organization.Id, "Building ZGR-A conflicts with UAT definition.");
            await database.SaveChangesAsync(cancellationToken);
            checks[currentCheck] = true;

            currentCheck = "Entrance";
            var entrance = await database.Entrances.SingleOrDefaultAsync(item => item.ZevId == zevA.Id
                && item.Code == "UL-A", cancellationToken);
            if (entrance is null)
            {
                entrance = new Entrance { OrganizationId = organization.Id, ZevId = zevA.Id,
                    BuildingId = building.Id, Code = "UL-A", Name = "Ulaz A" };
                database.Entrances.Add(entrance);
            }
            Require(entrance.Name == "Ulaz A" && entrance.IsActive && entrance.BuildingId == building.Id &&
                entrance.OrganizationId == organization.Id, "Entrance UL-A conflicts with UAT definition.");
            await database.SaveChangesAsync(cancellationToken);
            checks[currentCheck] = true;

            currentCheck = "Units";
            var firstUnit = await EnsureUnitAsync(organization.Id, zevA.Id, building.Id, entrance.Id,
                "UAT-A01", 60m, 2, cancellationToken);
            var secondUnit = await EnsureUnitAsync(organization.Id, zevA.Id, building.Id, entrance.Id,
                "UAT-A02", 80m, 3, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            checks[currentCheck] = true;

            currentCheck = "Ownership";
            var firstOwner = await EnsurePersonAsync(organization.Id, "UAT Test Vlasnik A", cancellationToken);
            var secondOwner = await EnsurePersonAsync(organization.Id, "UAT Test Vlasnik B", cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await EnsureOwnershipAsync(firstUnit, firstOwner, cancellationToken);
            await EnsureOwnershipAsync(secondUnit, secondOwner, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            checks[currentCheck] = true;

            currentCheck = "Funds";
            var regular = await EnsureFundAsync(organization.Id, zevA.Id, FundKind.Regular,
                "Redovno održavanje", cancellationToken);
            var reserve = await EnsureFundAsync(organization.Id, zevA.Id, FundKind.Reserve,
                "Rezervni fond", cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            checks[currentCheck] = true;

            currentCheck = "Billing items";
            var maintenance = await EnsureTariffAsync(organization.Id, zevA.Id, "UAT-OD1", "Redovno održavanje",
                TariffBasis.SquareMeter, .50m, regular.Id, 1, cancellationToken);
            var reserveItem = await EnsureTariffAsync(organization.Id, zevA.Id, "UAT-RF1", "Rezervni fond",
                TariffBasis.SquareMeter, .30m, reserve.Id, 2, cancellationToken);
            var cleaning = await EnsureTariffAsync(organization.Id, zevA.Id, "UAT-CIS1", "Čišćenje",
                TariffBasis.FixedUnit, 5m, regular.Id, 3, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            checks[currentCheck] = true;

            currentCheck = "Billing preview";
            var draft = new BillingDraftRequest([maintenance.Id, reserveItem.Id, cleaning.Id], null, null);
            var preview = await billing.PreviewSelectedUnitsAsync(zevA.Id, Period, DueDate, draft,
                [firstUnit.Id, secondUnit.Id], cancellationToken);
            Require(preview.Failure == BillingFailure.None,
                preview.Detail ?? $"Billing preview failed: {preview.Failure}.");
            unitA01 = preview.Units.Where(item => item.UnitCode == "UAT-A01").Sum(item => item.Total);
            unitA02 = preview.Units.Where(item => item.UnitCode == "UAT-A02").Sum(item => item.Total);
            total = preview.TotalAmount;
            Require(preview.Units.Count == 2 && preview.ChargeCount == 6 && unitA01 == 53m
                && unitA02 == 69m && total == 122m,
                $"Preview mismatch: UAT-A01={unitA01:0.00}, UAT-A02={unitA02:0.00}, total={total:0.00}.");
            checks[currentCheck] = true;

            if (!string.IsNullOrWhiteSpace(managerEmail) || !string.IsNullOrWhiteSpace(managerPassword))
            {
                currentCheck = "Manager access";
                checks[currentCheck] = false;
                await EnsureManagerAsync(organization.Id, zevA.Id, managerEmail, managerPassword, cancellationToken);
                checks[currentCheck] = true;
            }

            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(checks, null, unitA01, unitA02, total);
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return new(checks, $"{currentCheck}: {exception.Message} Changes in this run were rolled back.",
                unitA01, unitA02, total);
        }
    }

    private async Task<ZevEntity> EnsureZevAsync(Organization organization, string code, string name,
        CancellationToken cancellationToken)
    {
        var zev = await database.Zevs.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (zev is null)
        {
            zev = new ZevEntity { OrganizationId = organization.Id, Code = code, Name = name };
            database.Zevs.Add(zev);
        }
        Require(zev.OrganizationId == organization.Id && zev.Name == name && zev.IsActive,
            $"ZEV {code} conflicts with UAT definition.");
        return zev;
    }

    private async Task<PropertyUnit> EnsureUnitAsync(Guid organizationId, Guid zevId, Guid buildingId,
        Guid entranceId, string code, decimal area, int members, CancellationToken cancellationToken)
    {
        var unit = await database.PropertyUnits.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Code == code, cancellationToken);
        if (unit is null)
        {
            unit = new PropertyUnit { OrganizationId = organizationId, ZevId = zevId, BuildingId = buildingId,
                EntranceId = entranceId, Code = code, Type = PropertyUnitType.Apartment,
                AreaSquareMeters = area, Coefficient = 1m, RegisteredMembers = members };
            database.PropertyUnits.Add(unit);
        }
        Require(unit.OrganizationId == organizationId && unit.BuildingId == buildingId
            && unit.EntranceId == entranceId, $"Property unit {code} is linked to a different organization, building or entrance.");
        Require(unit.Type == PropertyUnitType.Apartment,
            $"Property unit {code} type is {unit.Type}; expected Apartment.");
        Require(unit.AreaSquareMeters == area,
            $"Property unit {code} area is {unit.AreaSquareMeters}; expected {area} m².");
        Require(unit.Coefficient == 1m,
            $"Property unit {code} coefficient is {unit.Coefficient}; expected 1.");
        Require(unit.RegisteredMembers == members,
            $"Property unit {code} has {unit.RegisteredMembers} members; expected {members}.");
        Require(unit.IsActive, $"Property unit {code} is inactive; expected active.");
        return unit;
    }

    private async Task<Person> EnsurePersonAsync(Guid organizationId, string name, CancellationToken cancellationToken)
    {
        var matches = await database.People.Where(item => item.OrganizationId == organizationId
            && item.DisplayName == name).ToListAsync(cancellationToken);
        Require(matches.Count <= 1, $"Multiple persons named {name} exist in organization TEST.");
        var person = matches.SingleOrDefault();
        if (person is null)
        {
            person = new Person { OrganizationId = organizationId, DisplayName = name,
                Kind = PersonKind.Individual };
            database.People.Add(person);
        }
        Require(person.Kind == PersonKind.Individual && person.IsActive,
            $"Person {name} conflicts with UAT definition.");
        return person;
    }

    private async Task EnsureOwnershipAsync(PropertyUnit unit, Person person, CancellationToken cancellationToken)
    {
        var history = await database.Ownerships.Where(item => item.PropertyUnitId == unit.Id)
            .ToListAsync(cancellationToken);
        var active = history.Where(item => item.ValidFrom <= Period
            && (item.ValidTo is null || item.ValidTo > Period)).ToList();
        if (active.Count == 0)
        {
            var ownership = new Ownership { OrganizationId = unit.OrganizationId, ZevId = unit.ZevId,
                PropertyUnitId = unit.Id, PersonId = person.Id, ValidFrom = Period, Share = 1m };
            Require(OwnershipTimeline.IsValid(history.Append(ownership)),
                $"Ownership history for {unit.Code} conflicts with October 2026.");
            unit.OwnershipRevision++;
            database.Ownerships.Add(ownership);
            return;
        }
        Require(active.Count == 1 && active[0].PersonId == person.Id && active[0].Share == 1m
            && active[0].ValidFrom == Period && active[0].ValidTo is null,
            $"Property unit {unit.Code} does not have the expected full active ownership from 2026-10-01.");
    }

    private async Task<Fund> EnsureFundAsync(Guid organizationId, Guid zevId, FundKind kind, string name,
        CancellationToken cancellationToken)
    {
        var fund = await database.Funds.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Kind == kind, cancellationToken);
        if (fund is null)
        {
            fund = new Fund { OrganizationId = organizationId, ZevId = zevId, Kind = kind, Name = name };
            database.Funds.Add(fund);
        }
        Require(fund.OrganizationId == organizationId && fund.Name == name && fund.IsActive,
            $"Fund {kind} conflicts with UAT definition.");
        return fund;
    }

    private async Task<TariffVersion> EnsureTariffAsync(Guid organizationId, Guid zevId, string code,
        string name, TariffBasis basis, decimal rate, Guid fundId, int order, CancellationToken cancellationToken)
    {
        var versions = await database.TariffVersions.Where(item => item.ZevId == zevId
            && item.Code == code).ToListAsync(cancellationToken);
        var activeVersions = versions.Where(item => item.IsActive && item.ValidFrom <= Period
            && (item.ValidTo is null || item.ValidTo > Period)).ToList();
        Require(activeVersions.Count <= 1, $"Multiple active tariff versions exist for code {code}.");
        var tariff = activeVersions.SingleOrDefault();
        Require(versions.Count == 0 || tariff is not null,
            $"Tariff {code} exists but is not valid for October 2026.");
        if (tariff is null)
        {
            tariff = new TariffVersion { OrganizationId = organizationId, ZevId = zevId, Code = code,
                Version = 1, Name = name, Basis = basis, Rate = rate, FundId = fundId,
                ValidFrom = Period, DisplayOrder = order, IsOptional = true };
            database.TariffVersions.Add(tariff);
        }
        Require(tariff.OrganizationId == organizationId && tariff.FundId == fundId,
            $"Tariff {code} is linked to a different organization or fund.");
        Require(tariff.Name == name && tariff.Basis == basis,
            $"Tariff {code} name or basis differs from UAT definition.");
        Require(tariff.Rate == rate,
            $"Tariff {code} rate is {tariff.Rate.ToString(CultureInfo.InvariantCulture)}; " +
            $"expected {rate.ToString(CultureInfo.InvariantCulture)}.");
        Require(tariff.ValidFrom == Period && tariff.ValidTo is null,
            $"Tariff {code} validity differs from 2026-10-01 without end date.");
        Require(tariff.IsActive && tariff.IsOptional && tariff.ShowOnInvoice,
            $"Tariff {code} must be active, optional and shown on invoice.");
        return tariff;
    }

    private async Task EnsureManagerAsync(Guid organizationId, Guid zevId, string? email, string? password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new InvalidOperationException("UAT manager requires email and a password of at least 12 characters.");
        var normalized = email.Trim().ToUpperInvariant();
        var user = await database.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalized,
            cancellationToken);
        if (user is null)
        {
            user = new AppUser { Email = email.Trim(), NormalizedEmail = normalized };
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            database.Users.Add(user);
        }
        else
            Require(passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
                != PasswordVerificationResult.Failed, "Existing UAT manager password does not match.");
        Require(user.IsActive && !user.IsSystemAdministrator,
            "UAT manager must be an active, non-system user.");
        await database.SaveChangesAsync(cancellationToken);
        var memberships = await database.Memberships.Where(item => item.UserId == user.Id).ToListAsync(cancellationToken);
        Require(memberships.All(item => !item.IsActive || item.OrganizationId == organizationId
            && item.ZevId == zevId && item.Role == MembershipRole.Manager),
            "UAT manager already has access outside TEST ZEV A.");
        if (!memberships.Any(item => item.IsActive && item.ZevId == zevId
            && item.Role == MembershipRole.Manager))
        {
            var membership = memberships.SingleOrDefault(item => item.ZevId == zevId
                && item.Role == MembershipRole.Manager);
            if (membership is null)
                database.Memberships.Add(new Membership { UserId = user.Id, OrganizationId = organizationId,
                    ZevId = zevId, Role = MembershipRole.Manager });
            else membership.IsActive = true;
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
