using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Maintenance;
using Zev.Api.Security;

namespace Zev.Api.Tests;

public sealed class MaintenanceTests
{
    [Fact]
    public void StatusFlowRejectsSkippingCompletion()
    {
        Assert.True(MaintenanceWorkflow.CanTransition(MaintenanceStatus.Open, MaintenanceStatus.InProgress));
        Assert.True(MaintenanceWorkflow.CanTransition(MaintenanceStatus.Completed, MaintenanceStatus.Verified));
        Assert.False(MaintenanceWorkflow.CanTransition(MaintenanceStatus.Open, MaintenanceStatus.Verified));
        Assert.False(MaintenanceWorkflow.CanTransition(MaintenanceStatus.Verified, MaintenanceStatus.InProgress));
    }

    [Fact]
    public async Task ScheduleGenerationIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        var organization = new Organization { Code = "O", Name = "Organization" };
        var zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id,
            Organization = organization, Code = "Z", Name = "ZEV" };
        var plan = new MaintenancePlan { OrganizationId = organization.Id, ZevId = zev.Id,
            Title = "Lift", Category = "Lift", FirstDueOn = new DateOnly(2026, 1, 1), FrequencyDays = 30 };
        database.AddRange(organization, zev, plan);
        await database.SaveChangesAsync();
        var service = new MaintenanceScheduleService(database);
        Assert.Equal(4, await service.EnsureUntilAsync(zev.Id, plan.Id, new DateOnly(2026, 4, 1), CancellationToken.None));
        Assert.Equal(0, await service.EnsureUntilAsync(zev.Id, plan.Id, new DateOnly(2026, 4, 1), CancellationToken.None));
        Assert.Equal(4, database.ScheduledMaintenances.Count());
    }

    [Fact]
    public async Task OwnerCanReportOnlyOwnedUnitAndRepresentativeCannotReadFinances()
    {
        var accessor = new HttpContextAccessor();
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ZevDbContext(options, accessor);
        await database.Database.EnsureCreatedAsync();
        var organization = new Organization { Code = "O", Name = "Organization" };
        var zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id,
            Organization = organization, Code = "Z", Name = "ZEV" };
        var building = new Building { OrganizationId = organization.Id, ZevId = zev.Id, Zev = zev,
            Code = "B", Name = "Building" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, Building = building, Code = "E", Name = "Entrance" };
        var owner = new AppUser { Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST" };
        var representative = new AppUser { Email = "rep@example.test", NormalizedEmail = "REP@EXAMPLE.TEST" };
        var person = new Person { OrganizationId = organization.Id, Organization = organization,
            UserId = owner.Id, User = owner, UserLinkStatus = UserPersonLinkStatus.Verified,
            DisplayName = "Owner" };
        var owned = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance, Code = "1", AreaSquareMeters = 50m };
        var other = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance, Code = "2", AreaSquareMeters = 50m };
        database.AddRange(organization, zev, building, entrance, owner, representative, person, owned, other);
        database.Memberships.Add(new Membership { UserId = representative.Id, User = representative,
            OrganizationId = organization.Id, ZevId = zev.Id, Role = MembershipRole.BuildingRepresentative });
        database.Ownerships.Add(new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
            PropertyUnitId = owned.Id, PersonId = person.Id, Person = person,
            ValidFrom = new DateOnly(2025, 1, 1), Share = 1m });
        await database.SaveChangesAsync();

        SetUser(accessor, owner);
        var effectiveAccess = new EffectiveAccessService(database, new ConfigurationBuilder().Build());
        var ownerAccess = new TenantAccess(database, accessor, effectiveAccess);
        Assert.True(await ownerAccess.CanReportTicketAsync(zev.Id, owned.Id, CancellationToken.None));
        Assert.False(await ownerAccess.CanReportTicketAsync(zev.Id, other.Id, CancellationToken.None));
        Assert.False(await ownerAccess.CanManageMaintenanceAsync(zev.Id, CancellationToken.None));

        SetUser(accessor, representative);
        var representativeAccess = new TenantAccess(database, accessor, effectiveAccess);
        Assert.True(await representativeAccess.CanManageMaintenanceAsync(zev.Id, CancellationToken.None));
        Assert.False(await representativeAccess.CanManageFinancesAsync(zev.Id, CancellationToken.None));
    }

    private static void SetUser(HttpContextAccessor accessor, AppUser user) =>
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id.ToString())], "test"))
        };
}
