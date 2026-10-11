namespace Zev.Api.Domain;

public enum PropertyUnitType
{
    Apartment,
    Commercial,
    Garage,
    Basement,
    Other,
    // Stored as text, so a new value needs no data migration; matches the PROPERTY_UNIT_TYPE code list.
    ParkingSpace
}

public enum PersonKind
{
    Individual,
    LegalEntity
}

public enum UserPersonLinkStatus { Pending, Verified, Rejected, Revoked }

public enum OccupantKind
{
    Resident,
    Tenant,
    OtherUser
}

public sealed class PropertyUnit : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid EntranceId { get; set; }
    public Entrance Entrance { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public PropertyUnitType Type { get; set; }
    public int? Floor { get; set; }
    public decimal AreaSquareMeters { get; set; }
    public decimal Coefficient { get; set; } = 1m;
    public int RegisteredMembers { get; set; }
    public bool IsActive { get; set; } = true;
    public int OwnershipRevision { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Person : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public Guid? UserId { get; set; }
    public AppUser? User { get; set; }
    public UserPersonLinkStatus UserLinkStatus { get; set; } = UserPersonLinkStatus.Pending;
    public Guid? UserLinkReviewedByUserId { get; set; }
    public DateTimeOffset? UserLinkReviewedUtc { get; set; }
    public PersonKind Kind { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? TaxIdentifier { get; set; }
    public string? AddressLine { get; set; }
    public string? CityMunicipality { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class Ownership : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PropertyUnitId { get; set; }
    public PropertyUnit PropertyUnit { get; set; } = null!;
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal Share { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Occupancy : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PropertyUnitId { get; set; }
    public PropertyUnit PropertyUnit { get; set; } = null!;
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public OccupantKind Kind { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}
