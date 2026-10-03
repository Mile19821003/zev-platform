namespace Zev.Api.Domain;

public enum FundKind { Regular, Reserve }
public enum TariffBasis { SquareMeter, FixedUnit, RegisteredMember, Extraordinary, ManualAmount, OneTime }
public enum BillingRunStatus { Completed }
public enum FundTransactionKind { Payment, Expense, Reversal }
public enum InvoiceDocumentStatus { Draft, Issued, Cancelled, Corrected }
public enum InvoiceDiscountType { None, Percentage, Fixed }
public enum FinancialDocumentType { Storno, Credit, Debit }
public enum FinancialDocumentSeries { Storno, Correction }

public sealed class Invoice : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid BillingRunId { get; set; }
    public Guid PropertyUnitId { get; set; }
    public Guid PersonId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int NumberYear { get; set; }
    public int NumberSequence { get; set; }
    public DateOnly Period { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public InvoiceDocumentStatus DocumentStatus { get; set; } = InvoiceDocumentStatus.Issued;
    public string Currency { get; set; } = "BAM";
    public decimal PreviousBalanceSnapshot { get; set; }
    public decimal CurrentChargeTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalDueAtIssue { get; set; }
    public string PaymentReferenceSnapshot { get; set; } = string.Empty;
    public string ZevNameSnapshot { get; set; } = string.Empty;
    public string? ZevAddressSnapshot { get; set; }
    public string? ZevTaxIdentifierSnapshot { get; set; }
    public string? ZevEmailSnapshot { get; set; }
    public string? ZevPhoneSnapshot { get; set; }
    public string PersonNameSnapshot { get; set; } = string.Empty;
    public string? PersonAddressSnapshot { get; set; }
    public string UnitCodeSnapshot { get; set; } = string.Empty;
    public string UnitTypeSnapshot { get; set; } = string.Empty;
    public decimal UnitAreaSnapshot { get; set; }
    public string? UnitAddressSnapshot { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset IssuedUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? IssuedByUserId { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class InvoiceLine : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public Guid ChargeId { get; set; }
    public Charge Charge { get; set; } = null!;
    public Guid? TariffVersionId { get; set; }
    public Guid FundId { get; set; }
    public string CodeSnapshot { get; set; } = string.Empty;
    public string DescriptionSnapshot { get; set; } = string.Empty;
    public string UnitOfMeasureSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal OwnershipShareSnapshot { get; set; }
    public InvoiceDiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal AmountBeforeDiscount { get; set; }
    public decimal FinalAmount { get; set; }
    public int SortOrder { get; set; }
    public bool ShowOnInvoice { get; set; } = true;
    public string? NoteSnapshot { get; set; }
    public string FundNameSnapshot { get; set; } = string.Empty;
    public string? BankAccountSnapshot { get; set; }
    public string? BankNameSnapshot { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class FinancialDocument : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid OriginalInvoiceId { get; set; }
    public Invoice OriginalInvoice { get; set; } = null!;
    public Guid RequestId { get; set; }
    public FinancialDocumentType Type { get; set; }
    public InvoiceDocumentStatus DocumentStatus { get; set; } = InvoiceDocumentStatus.Issued;
    public FinancialDocumentSeries Series { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public int NumberYear { get; set; }
    public int NumberSequence { get; set; }
    public DateOnly IssueDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal SignedAmount { get; set; }
    public string Currency { get; set; } = "BAM";
    public string ZevNameSnapshot { get; set; } = string.Empty;
    public string PersonNameSnapshot { get; set; } = string.Empty;
    public string UnitCodeSnapshot { get; set; } = string.Empty;
    public string OriginalInvoiceNumberSnapshot { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<FinancialDocumentLine> Lines { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class FinancialDocumentLine : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid DocumentId { get; set; }
    public FinancialDocument Document { get; set; } = null!;
    public Guid? OriginalInvoiceLineId { get; set; }
    public Guid ChargeId { get; set; }
    public Charge Charge { get; set; } = null!;
    public Guid FundId { get; set; }
    public string FundNameSnapshot { get; set; } = string.Empty;
    public string DescriptionSnapshot { get; set; } = string.Empty;
    public decimal SignedAmount { get; set; }
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Fund : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Zev Zev { get; set; } = null!;
    public FundKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool RequiresBankAccountForInvoice { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class TariffVersion : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TariffBasis Basis { get; set; }
    public decimal Rate { get; set; }
    public Guid FundId { get; set; }
    public Fund Fund { get; set; } = null!;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsOptional { get; set; }
    public bool ShowOnInvoice { get; set; } = true;
    public int DisplayOrder { get; set; }
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class BillingRun : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public DateOnly Period { get; set; }
    public DateOnly DueDate { get; set; }
    public BillingRunStatus Status { get; set; } = BillingRunStatus.Completed;
    public int ChargeCount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Charge : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PropertyUnitId { get; set; }
    public PropertyUnit PropertyUnit { get; set; } = null!;
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public Guid FundId { get; set; }
    public Fund Fund { get; set; } = null!;
    public Guid? TariffVersionId { get; set; }
    public TariffVersion? TariffVersion { get; set; }
    public Guid? BillingRunId { get; set; }
    public BillingRun? BillingRun { get; set; }
    public Guid? FinancialDocumentId { get; set; }
    public Guid? ReversesChargeId { get; set; }
    public Charge? ReversesCharge { get; set; }
    public DateOnly? EffectiveOn { get; set; }
    public DateOnly Period { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal BasisQuantity { get; set; }
    public decimal RateSnapshot { get; set; }
    public decimal OwnershipShareSnapshot { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public bool IsManualOverride { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class FundTransaction : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid FundId { get; set; }
    public Fund Fund { get; set; } = null!;
    public FundTransactionKind Kind { get; set; }
    public decimal SignedAmount { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? ReversesTransactionId { get; set; }
    public FundTransaction? ReversesTransaction { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}
