namespace Zev.Api.Domain;

public enum MembershipRole
{
    Manager,
    Accountant,
    BuildingRepresentative,
    Owner,
    OrganizationAdministrator
}

public enum AccessEffect { Grant, Deny }

public sealed class AccessOverride : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public AccessEffect Effect { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidToUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? RevokedByUserId { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? RevokeReason { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Permission : ITenantRecord
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    Guid? ITenantRecord.OrganizationId => null;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class RolePermission : ITenantRecord
{
    public MembershipRole Role { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public Permission Permission { get; set; } = null!;
    Guid? ITenantRecord.OrganizationId => null;
    Guid? ITenantRecord.ZevId => null;
}

public interface ITenantRecord
{
    Guid? OrganizationId { get; }
    Guid? ZevId { get; }
}

public sealed class AppUser : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public int SecurityVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystemAdministrator { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => null;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class Organization : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AddressLine { get; set; }
    public string? CityMunicipality { get; set; }
    public string? TaxIdentifier { get; set; }
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateOnly? ActivatedOn { get; set; }
    public DateOnly? DeactivatedOn { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => Id;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class Zev : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AddressLine { get; set; }
    public string? CityMunicipality { get; set; }
    public string? TaxIdentifier { get; set; }
    public string? PrimaryBankAccount { get; set; }
    public string? ReserveFundBankAccount { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public DateOnly? FoundedOn { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AutoApplyPaymentCredit { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => Id;
}

public sealed class Building : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Zev Zev { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AddressLine { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Entrance : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid BuildingId { get; set; }
    public Building Building { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Membership : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public Guid? ZevId { get; set; }
    public Zev? Zev { get; set; }
    public MembershipRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class RefreshSession : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public int SecurityVersion { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedUtc { get; set; }
    public Guid? ReplacedById { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => null;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? ZevId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;
}
