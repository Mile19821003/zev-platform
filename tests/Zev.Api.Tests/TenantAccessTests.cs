using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using ZevEntity = global::Zev.Api.Domain.Zev;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Tests;

public sealed class TenantAccessTests
{
    [Fact]
    public async Task OwnerCannotReadAnotherZevByChangingId()
    {
        using var fixture = new AccessFixture();
        var owner = fixture.AddUser();
        var first = fixture.AddZev();
        var second = fixture.AddZev();
        fixture.AddOwner(owner, first);
        await fixture.SaveAsync();

        var access = fixture.AccessAs(owner);
        Assert.True(await access.CanReadZevAsync(first.Id, CancellationToken.None));
        Assert.False(await access.CanReadZevAsync(second.Id, CancellationToken.None));
        Assert.False(await access.CanReadOrganizationAsync(first.OrganizationId, CancellationToken.None));
    }

    [Fact]
    public async Task OwnerFunctionalGrantNeverBecomesTenantWideStaffAccess()
    {
        using var fixture = new AccessFixture();
        var owner = fixture.AddUser();
        var ownZev = fixture.AddZev();
        var otherZev = fixture.AddZev();
        fixture.AddOwner(owner, ownZev);
        fixture.AddOverride(owner, ownZev, AccessEffect.Grant, DateTimeOffset.UtcNow.AddDays(1),
            permissionKey: "BankAccounts.View");
        await fixture.SaveAsync();
        var access = fixture.AccessAs(owner);
        Assert.True(await access.HasPermissionAsync(ownZev.Id, "Finance.View", CancellationToken.None, staffOnly: false));
        Assert.False(await access.HasPermissionAsync(ownZev.Id, "Finance.View", CancellationToken.None));
        Assert.False(await access.HasPermissionAsync(ownZev.Id, "BankAccounts.View", CancellationToken.None));
        Assert.False(await access.HasPermissionAsync(otherZev.Id, "Finance.View", CancellationToken.None, staffOnly: false));
    }

    [Fact]
    public async Task GranularDenyOverridesRoleAndGrantOnlyInsideSelectedZev()
    {
        using var fixture = new AccessFixture();
        var manager = fixture.AddUser();
        var ownZev = fixture.AddZev();
        var otherZev = fixture.AddZev();
        fixture.AddMembership(manager, ownZev, MembershipRole.Manager);
        fixture.AddOverride(manager, ownZev, AccessEffect.Grant, DateTimeOffset.UtcNow.AddDays(1),
            permissionKey: "BankAccounts.Create");
        fixture.AddOverride(manager, ownZev, AccessEffect.Deny, DateTimeOffset.UtcNow.AddDays(1),
            permissionKey: "BankAccounts.Create");
        await fixture.SaveAsync();
        var access = fixture.AccessAs(manager);
        Assert.False(await access.HasPermissionAsync(ownZev.Id, "BankAccounts.Create", CancellationToken.None));
        Assert.False(await access.HasPermissionAsync(otherZev.Id, "BankAccounts.Create", CancellationToken.None));
        Assert.True(await access.HasPermissionAsync(ownZev.Id, "BankAccounts.View", CancellationToken.None));
    }

    [Fact]
    public async Task OrganizationManagerCanReadOnlyOwnOrganizationsZevs()
    {
        using var fixture = new AccessFixture();
        var manager = fixture.AddUser();
        var first = fixture.AddZev();
        var second = fixture.AddZev(first.OrganizationId);
        var outside = fixture.AddZev();
        fixture.AddMembership(manager, first, MembershipRole.OrganizationAdministrator, organizationWide: true);
        await fixture.SaveAsync();

        var access = fixture.AccessAs(manager);
        Assert.True(await access.CanReadOrganizationAsync(first.OrganizationId, CancellationToken.None));
        Assert.True(await access.CanReadZevAsync(first.Id, CancellationToken.None));
        Assert.True(await access.CanReadZevAsync(second.Id, CancellationToken.None));
        Assert.False(await access.CanReadZevAsync(outside.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RevokedMembershipImmediatelyRemovesAccess()
    {
        using var fixture = new AccessFixture();
        var user = fixture.AddUser();
        var zev = fixture.AddZev();
        var membership = fixture.AddMembership(user, zev, MembershipRole.BuildingRepresentative);
        await fixture.SaveAsync();
        var access = fixture.AccessAs(user);
        Assert.True(await access.CanReadZevAsync(zev.Id, CancellationToken.None));

        membership.IsActive = false;
        await fixture.SaveAsync();
        Assert.False(await access.CanReadZevAsync(zev.Id, CancellationToken.None));
    }

    [Fact]
    public async Task OwnerAccessRequiresVerifiedLinkAndExpiresWithOwnership()
    {
        using var fixture = new AccessFixture();
        var user = fixture.AddUser();
        var zev = fixture.AddZev();
        var ownership = fixture.AddOwner(user, zev, verified: false);
        await fixture.SaveAsync();
        var access = fixture.AccessAs(user);
        Assert.False(await access.CanReadZevAsync(zev.Id, CancellationToken.None));
        ownership.Person.UserLinkStatus = UserPersonLinkStatus.Verified;
        await fixture.SaveAsync();
        Assert.True(await access.CanReadZevAsync(zev.Id, CancellationToken.None));
        ownership.ValidTo = DateOnly.FromDateTime(DateTime.UtcNow);
        await fixture.SaveAsync();
        Assert.False(await access.CanReadZevAsync(zev.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ExplicitDenyBeatsOwnerAndGrantExpires()
    {
        using var fixture = new AccessFixture();
        var user = fixture.AddUser();
        var zev = fixture.AddZev();
        fixture.AddOwner(user, zev);
        await fixture.SaveAsync();
        var access = fixture.AccessAs(user);
        fixture.AddOverride(user, zev, AccessEffect.Deny, DateTimeOffset.UtcNow.AddDays(1));
        await fixture.SaveAsync();
        Assert.False(await access.CanReadZevAsync(zev.Id, CancellationToken.None));
    }

    [Fact]
    public async Task TwoVerifiedCoOwnersBothGetAccessWithoutPrivatePersonData()
    {
        using var fixture = new AccessFixture();
        var first = fixture.AddUser();
        var second = fixture.AddUser();
        var zev = fixture.AddZev();
        var ownership = fixture.AddOwner(first, zev);
        ownership.Share = 0.5m;
        fixture.AddCoOwner(second, ownership);
        await fixture.SaveAsync();
        Assert.True(await fixture.AccessAs(first).CanReadZevAsync(zev.Id, CancellationToken.None));
        Assert.True(await fixture.AccessAs(second).CanReadZevAsync(zev.Id, CancellationToken.None));
        Assert.False(await fixture.AccessAs(second).CanReadOrganizationAsync(zev.OrganizationId, CancellationToken.None));
    }

    [Fact]
    public async Task HistoricalAccessIsOptInAndDoesNotRestoreZevRead()
    {
        using var fixture = new AccessFixture();
        var user = fixture.AddUser();
        var zev = fixture.AddZev();
        var ownership = fixture.AddOwner(user, zev);
        ownership.ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var lastDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        ownership.ValidTo = lastDay;
        await fixture.SaveAsync();
        var disabled = fixture.AccessAs(user);
        Assert.False(await disabled.CanReadHistoricalPersonAsync(zev.Id, ownership.PersonId,
            lastDay.AddDays(-15), CancellationToken.None));
        var enabled = fixture.AccessAs(user, historical: true);
        Assert.False(await enabled.CanReadZevAsync(zev.Id, CancellationToken.None));
        Assert.True(await enabled.CanReadHistoricalPersonAsync(zev.Id, ownership.PersonId,
            lastDay.AddDays(-15), CancellationToken.None));
        Assert.False(await enabled.CanReadHistoricalPersonAsync(zev.Id, ownership.PersonId,
            DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None));
    }

    [Fact]
    public async Task ManualReadGrantIsScopedAndExpires()
    {
        using var fixture = new AccessFixture();
        var user = fixture.AddUser();
        var first = fixture.AddZev();
        var second = fixture.AddZev();
        fixture.AddOverride(user, first, AccessEffect.Grant, DateTimeOffset.UtcNow.AddDays(1));
        await fixture.SaveAsync();
        var access = fixture.AccessAs(user);
        Assert.True(await access.CanReadZevAsync(first.Id, CancellationToken.None));
        Assert.False(await access.CanReadZevAsync(second.Id, CancellationToken.None));
        fixture.AddOverride(user, first, AccessEffect.Deny, DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-2));
        await fixture.SaveAsync();
        Assert.True(await access.CanReadZevAsync(first.Id, CancellationToken.None));
    }

    private sealed class AccessFixture : IDisposable
    {
        private readonly HttpContextAccessor httpContextAccessor = new();
        private readonly ZevDbContext database;

        public AccessFixture()
        {
            var options = new DbContextOptionsBuilder<ZevDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            database = new ZevDbContext(options, httpContextAccessor);
            database.Database.EnsureCreated();
        }

        public AppUser AddUser()
        {
            var user = new AppUser { Email = $"{Guid.NewGuid()}@example.invalid", NormalizedEmail = Guid.NewGuid().ToString() };
            database.Users.Add(user);
            return user;
        }

        public ZevEntity AddZev(Guid? organizationId = null)
        {
            var organization = organizationId is null
                ? new Organization { Code = Guid.NewGuid().ToString(), Name = "Test organization" }
                : database.Organizations.Local.Single(item => item.Id == organizationId);
            if (organizationId is null) database.Organizations.Add(organization);
            var zev = new ZevEntity { Organization = organization, OrganizationId = organization.Id, Code = Guid.NewGuid().ToString(), Name = "Test ZEV" };
            database.Zevs.Add(zev);
            return zev;
        }

        public Membership AddMembership(AppUser user, ZevEntity zev, MembershipRole role, bool organizationWide = false)
        {
            var membership = new Membership
            {
                User = user,
                UserId = user.Id,
                OrganizationId = zev.OrganizationId,
                ZevId = organizationWide ? null : zev.Id,
                Role = role
            };
            database.Memberships.Add(membership);
            return membership;
        }

        public Ownership AddOwner(AppUser user, ZevEntity zev, bool verified = true)
        {
            var building = new Building { OrganizationId = zev.OrganizationId, ZevId = zev.Id,
                Code = Guid.NewGuid().ToString(), Name = "Building" };
            var entrance = new Entrance { OrganizationId = zev.OrganizationId, ZevId = zev.Id,
                BuildingId = building.Id, Code = Guid.NewGuid().ToString(), Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = zev.OrganizationId, ZevId = zev.Id,
                BuildingId = building.Id, EntranceId = entrance.Id, Code = Guid.NewGuid().ToString(),
                AreaSquareMeters = 50m };
            var person = new Person { OrganizationId = zev.OrganizationId, UserId = user.Id,
                UserLinkStatus = verified ? UserPersonLinkStatus.Verified : UserPersonLinkStatus.Pending,
                DisplayName = "Owner" };
            var ownership = new Ownership { OrganizationId = zev.OrganizationId, ZevId = zev.Id,
                PropertyUnitId = unit.Id, PersonId = person.Id, Person = person, PropertyUnit = unit,
                ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Share = 1m };
            database.Buildings.Add(building);
            database.Entrances.Add(entrance);
            database.PropertyUnits.Add(unit);
            database.People.Add(person);
            database.Ownerships.Add(ownership);
            return ownership;
        }

        public void AddCoOwner(AppUser user, Ownership existing)
        {
            var person = new Person { OrganizationId = existing.OrganizationId, UserId = user.Id,
                UserLinkStatus = UserPersonLinkStatus.Verified, DisplayName = "Co-owner" };
            database.People.Add(person);
            database.Ownerships.Add(new Ownership { OrganizationId = existing.OrganizationId,
                ZevId = existing.ZevId, PropertyUnitId = existing.PropertyUnitId,
                Person = person, ValidFrom = existing.ValidFrom, Share = 0.5m });
        }

        public void AddOverride(AppUser user, ZevEntity zev, AccessEffect effect, DateTimeOffset? validTo,
            DateTimeOffset? validFrom = null, string permissionKey = AccessPermissions.Read)
        {
            database.AccessOverrides.Add(new AccessOverride { UserId = user.Id, OrganizationId = zev.OrganizationId,
                ZevId = zev.Id, PermissionKey = permissionKey, Effect = effect,
                ValidFromUtc = validFrom ?? DateTimeOffset.UtcNow.AddMinutes(-1), ValidToUtc = validTo,
                CreatedByUserId = user.Id, Reason = "Test" });
        }

        public Task SaveAsync() => database.SaveChangesAsync();

        public TenantAccess AccessAs(AppUser user, bool historical = false)
        {
            httpContextAccessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id.ToString())], "test"))
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Privacy:HistoricalSelfAccess"] = historical.ToString()
            }).Build();
            return new TenantAccess(database, httpContextAccessor,
                new EffectiveAccessService(database, configuration));
        }

        public void Dispose() => database.Dispose();
    }
}
