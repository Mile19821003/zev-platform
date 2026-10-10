using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Zev.Api.Tests;

// Throws the armed exception on the next SaveChanges, then disarms; lets tests simulate provider failures.
internal sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public Exception? Next { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Next is { } exception)
        {
            Next = null;
            throw exception;
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
