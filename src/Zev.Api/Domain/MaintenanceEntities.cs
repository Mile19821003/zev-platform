namespace Zev.Api.Domain;

public enum MaintenanceStatus { Open, InProgress, Completed, Verified, Cancelled }
public enum MaintenancePriority { Low, Normal, High, Urgent }
public enum ScheduleStatus { Due, InProgress, Completed, Cancelled }
public enum WorkOrderAttachmentType { Invoice, PhotoBefore, PhotoAfter, WorkReport }

public sealed class MaintenanceTicket : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid? PropertyUnitId { get; set; }
    public Guid ReportedByUserId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public MaintenancePriority Priority { get; set; } = MaintenancePriority.Normal;
    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Open;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    // "Novo" until someone who handles maintenance opens it (bell in the header, badge in the list).
    public bool IsReadByAdmin { get; set; }
    public DateTimeOffset? ReadByAdminAt { get; set; }
    public Guid? ReadByAdminUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class MaintenanceTicketFile : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid TicketId { get; set; }
    public Guid StoredFileId { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class WorkOrder : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? SupplierId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MaintenancePriority Priority { get; set; } = MaintenancePriority.Normal;
    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Open;
    public decimal? CostAmount { get; set; }
    public DateOnly? DueOn { get; set; }
    // Contractor: a supplier from the code list, or an external contractor by name and phone.
    public string? ContractorName { get; set; }
    public string? ContractorPhone { get; set; }
    // Finance of the job; the expense is created on completion when requested (ExpenseId then points to it).
    public string? InvoiceNumber { get; set; }
    public FundKind? ExpenseFundKind { get; set; }
    public Guid? ExpenseId { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public string? WorkDescription { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

// Documents of a work order (invoice PDF, photos before / after, work report), up to 10 MB each, in the file storage.
public sealed class WorkOrderAttachment : ITenantRecord
{
    public const int MaxBytes = 10_000_000;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrderAttachmentType AttachmentType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int Length { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UploadedByUserId { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class WorkOrderFile : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid StoredFileId { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class MaintenanceStatusEvent : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? WorkOrderId { get; set; }
    public MaintenanceStatus Status { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset ChangedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class MaintenancePlan : ITenantRecord, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int FrequencyDays { get; set; }
    public DateOnly FirstDueOn { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class ScheduledMaintenance : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PlanId { get; set; }
    public MaintenancePlan Plan { get; set; } = null!;
    public DateOnly DueOn { get; set; }
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Due;
    public Guid? WorkOrderId { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}
