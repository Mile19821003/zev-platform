namespace Zev.Api.Domain;

public enum MaintenanceStatus { Open, InProgress, Completed, Verified, Cancelled }
public enum MaintenancePriority { Low, Normal, High, Urgent }
public enum ScheduleStatus { Due, InProgress, Completed, Cancelled }

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
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
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

public sealed class MaintenancePlan : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int FrequencyDays { get; set; }
    public DateOnly FirstDueOn { get; set; }
    public bool IsActive { get; set; } = true;
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
