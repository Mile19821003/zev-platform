namespace Zev.Api.Domain;

public enum BankImportStatus { Imported, PartiallyReconciled, Reconciled }
public enum BankTransactionStatus { Pending, Matched, ManualReview, Ignored, PartiallyMatched, Returned }
public enum ReconciliationStatus { Matched, Ambiguous, NoMatch, Rejected, Reversed, PartiallyMatched }
public enum PaymentStatus { Posted, Reversed }
public enum PaymentSourceType { BankTransaction, Manual }

public sealed class BankAccount : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid FundId { get; set; }
    public Fund Fund { get; set; } = null!;
    public string AccountNumber { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class BankStatementImport : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid BankAccountId { get; set; }
    public BankAccount BankAccount { get; set; } = null!;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public BankImportStatus Status { get; set; } = BankImportStatus.Imported;
    public int TransactionCount { get; set; }
    // Balances and period as stated in the file; null when the format does not carry them.
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public DateOnly? StatementFrom { get; set; }
    public DateOnly? StatementTo { get; set; }
    public Guid? ImportedByUserId { get; set; }
    public DateTimeOffset ImportedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class BankTransaction : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid ImportId { get; set; }
    public BankStatementImport Import { get; set; } = null!;
    public int Sequence { get; set; }
    public DateOnly BookingDate { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string? Counterparty { get; set; }
    public string? Description { get; set; }
    public BankTransactionStatus Status { get; set; } = BankTransactionStatus.Pending;
    public byte[] RowVersion { get; set; } = [];
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class Payment : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid FundId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public PaymentSourceType SourceType { get; set; } = PaymentSourceType.BankTransaction;
    public Guid? BankTransactionId { get; set; }
    public BankTransaction? BankTransaction { get; set; }
    public Guid? RequestId { get; set; }
    public string? ManualReason { get; set; }
    public string? ManualMethod { get; set; }
    public string? ManualReference { get; set; }
    public string? ManualDescription { get; set; }
    public Guid? ManualUnitId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Posted;
    public DateOnly? ReversedOn { get; set; }
    public string? ReversalReason { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateOnly PaidOn { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class PaymentAllocation : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public Guid ChargeId { get; set; }
    public Charge Charge { get; set; } = null!;
    public decimal Amount { get; set; }
    public Guid? ReversesAllocationId { get; set; }
    public PaymentAllocation? ReversesAllocation { get; set; }
    public Guid? FinancialDocumentId { get; set; }
    public Guid? OperationId { get; set; }
    public string? ReversalReason { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class PaymentIdentificationHistory : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public Guid? PreviousPersonId { get; set; }
    public Guid NewPersonId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ChangedByUserId { get; set; }
    public DateTimeOffset ChangedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

public sealed class ReconciliationAttempt : ITenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid BankTransactionId { get; set; }
    public BankTransaction BankTransaction { get; set; } = null!;
    public Guid? PaymentId { get; set; }
    public string Rule { get; set; } = string.Empty;
    public ReconciliationStatus Status { get; set; }
    public string Result { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}

// Stable payment account of one person for one property unit; Number is the basis of the short
// numeric payment reference (poziv na broj), see PaymentReferenceFormat.
public sealed class PaymentAccount : ITenantRecord
{
    public long Number { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ZevId { get; set; }
    public Guid PropertyUnitId { get; set; }
    public Guid PersonId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    Guid? ITenantRecord.OrganizationId => OrganizationId;
    Guid? ITenantRecord.ZevId => ZevId;
}
