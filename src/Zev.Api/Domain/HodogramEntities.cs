namespace Zev.Api.Domain;

public enum RaciValue { R, A, C, I }

public sealed class Hodogram : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public int Revision { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class HodogramPhase : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid HodogramId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class HodogramStep : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid HodogramId { get; set; }
    public Guid PhaseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class HodogramAssignment : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid HodogramId { get; set; }
    public Guid StepId { get; set; }
    public MembershipRole Role { get; set; }
    public RaciValue Value { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => null;
}
