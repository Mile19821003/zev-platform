using Zev.Api.Domain;

namespace Zev.Api.Payments;

public static class PaymentAllocationStatus
{
    public static string Derive(decimal amount, decimal allocated, Guid? personId,
        PaymentStatus status, BankTransactionStatus? bankStatus)
    {
        if (status == PaymentStatus.Reversed) return "REVERSED";
        if (personId is null) return "MANUAL_REVIEW";
        if (allocated >= amount) return "FULLY_ALLOCATED";
        if (allocated > 0m) return "PARTIALLY_ALLOCATED_CREDIT_REMAINS";
        return bankStatus == BankTransactionStatus.ManualReview
            ? "MANUAL_REVIEW" : "IDENTIFIED_UNALLOCATED";
    }
}
