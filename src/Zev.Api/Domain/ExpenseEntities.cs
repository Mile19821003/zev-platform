namespace Zev.Api.Domain;

public enum ExpenseStatus { Draft, Posted, Reversed }
public enum ReminderKind { Payment, BeforeLawsuit }
public enum LegalCaseStatus { Open, Filed, InProgress, Settled, Closed }

public sealed class StoredFile : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Length { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public DateTimeOffset UploadedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Supplier : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TaxIdentifier { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => null;
}

public sealed class Expense : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid FundId { get; set; }
    public Fund Fund { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public Guid? DocumentId { get; set; }
    public StoredFile? Document { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Draft;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Reminder : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public ReminderKind Kind { get; set; }
    public decimal PrincipalSnapshot { get; set; }
    public decimal InterestSnapshot { get; set; }
    public DateOnly AsOf { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class InterestRatePolicy : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal AnnualRatePercent { get; set; }
    public int DaysPerYear { get; set; }
    public bool IsActive { get; set; } = true;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class LegalCase : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public decimal PrincipalAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal CostsAmount { get; set; }
    public decimal RecoveredAmount { get; set; }
    public string? Lawyer { get; set; }
    public string? Court { get; set; }
    public string? CaseNumber { get; set; }
    public DateOnly? FiledOn { get; set; }
    public LegalCaseStatus Status { get; set; } = LegalCaseStatus.Open;
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class LegalCaseFile : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid LegalCaseId { get; set; }
    public Guid StoredFileId { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}
