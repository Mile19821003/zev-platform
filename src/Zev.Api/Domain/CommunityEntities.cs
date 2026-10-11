namespace Zev.Api.Domain;

public enum AudienceScope { Zev, Building, Entrance, Role }
// Stored as text; the values equal the codes of the DOCUMENT_CATEGORY code list.
public enum ManagedDocumentType { Contract, Policy, Statute, Minutes, Financial, Legal, Other, InsurancePolicy, Decision }
public enum DocumentVisibility { ManagersOnly, ZevMembers, PersonOnly, PropertyUnit, Role }
public enum BallotKind { Survey, Formal }
public enum VotingMode { OneUnitOneVote, Weighted }
public enum VotingWeightBasis { UnitArea, UnitCoefficient }
public enum ApprovalDenominator { Eligible, Participating, YesNo }
public enum BallotStatus { Draft, Open, Closed }
public enum VoteChoice { Yes, No, Abstain }

public sealed class Announcement : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PublishedByUserId { get; set; }
    public AudienceScope Scope { get; set; } = AudienceScope.Zev;
    public Guid? BuildingId { get; set; }
    public Guid? EntranceId { get; set; }
    public MembershipRole? TargetRole { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset PublishedFromUtc { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class AnnouncementFile : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid AnnouncementId { get; set; }
    public Guid StoredFileId { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class ManagedDocument : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid StoredFileId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ManagedDocumentType Type { get; set; }
    public DocumentVisibility Visibility { get; set; }
    public Guid? PersonId { get; set; }
    public Guid? PropertyUnitId { get; set; }
    public MembershipRole? TargetRole { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Ballot : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public BallotKind Kind { get; set; }
    public VotingMode Mode { get; set; }
    public VotingWeightBasis WeightBasis { get; set; }
    public ApprovalDenominator ApprovalDenominator { get; set; }
    public decimal QuorumPercent { get; set; }
    public decimal ApprovalPercent { get; set; }
    public DateTimeOffset OpensUtc { get; set; }
    public DateTimeOffset ClosesUtc { get; set; }
    public BallotStatus Status { get; set; } = BallotStatus.Draft;
    public decimal? EligibleWeight { get; set; }
    public decimal? CastWeight { get; set; }
    public decimal? YesWeight { get; set; }
    public decimal? NoWeight { get; set; }
    public decimal? AbstainWeight { get; set; }
    public bool? QuorumMet { get; set; }
    public bool? Approved { get; set; }
    public DateTimeOffset? ClosedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class BallotEligibility : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid BallotId { get; set; }
    public Guid PropertyUnitId { get; set; }
    public Guid PersonId { get; set; }
    public decimal Weight { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Vote : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid BallotId { get; set; }
    public Guid PropertyUnitId { get; set; }
    public Guid PersonId { get; set; }
    public Guid CastByUserId { get; set; }
    public VoteChoice Choice { get; set; }
    public decimal WeightSnapshot { get; set; }
    public DateTimeOffset CastUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}
