using System.Data;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Finance;

public sealed class ExpensePostingService(ZevDbContext database)
{
    public async Task<bool> PostAsync(Guid zevId, Guid expenseId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var expense = await database.Expenses.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Id == expenseId && item.Status == ExpenseStatus.Draft, cancellationToken);
        if (expense is null) return false;
        expense.Status = ExpenseStatus.Posted;
        database.FundTransactions.Add(new FundTransaction
        {
            OrganizationId = expense.OrganizationId, ZevId = zevId, FundId = expense.FundId,
            Kind = FundTransactionKind.Expense, SignedAmount = -expense.Amount,
            SourceId = expense.Id, EffectiveOn = expense.InvoiceDate
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception problem) when (DbUpdateErrors.IsConcurrentWriteConflict(problem)) { return false; }
    }

    public async Task<bool> ReverseAsync(Guid zevId, Guid expenseId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var expense = await database.Expenses.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Id == expenseId && item.Status == ExpenseStatus.Posted, cancellationToken);
        if (expense is null) return false;
        var original = await database.FundTransactions.SingleOrDefaultAsync(item => item.ZevId == zevId
            && item.Kind == FundTransactionKind.Expense && item.SourceId == expense.Id, cancellationToken);
        if (original is null || await database.FundTransactions.AnyAsync(item => item.ReversesTransactionId == original.Id,
            cancellationToken)) return false;
        expense.Status = ExpenseStatus.Reversed;
        database.FundTransactions.Add(new FundTransaction
        {
            OrganizationId = expense.OrganizationId, ZevId = zevId, FundId = expense.FundId,
            Kind = FundTransactionKind.Reversal, SignedAmount = expense.Amount,
            SourceId = expense.Id, ReversesTransactionId = original.Id,
            EffectiveOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception problem) when (DbUpdateErrors.IsConcurrentWriteConflict(problem)) { return false; }
    }
}
