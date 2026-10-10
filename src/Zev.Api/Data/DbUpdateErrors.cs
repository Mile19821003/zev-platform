using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Zev.Api.Data;

public static class DbUpdateErrors
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int DeadlockVictim = 1205;

    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation };

    // Outcomes of a concurrent writer winning the race: stale row version, duplicate key on an
    // idempotency index, or being chosen as deadlock victim inside a serializable transaction.
    // Anything else (FK/CHECK violations, truncation, timeouts, connectivity) is a defect and must propagate.
    public static bool IsConcurrentWriteConflict(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => true,
        DbUpdateException update => IsUniqueViolation(update) || IsDeadlockVictim(update),
        // SqlServerExecutionStrategy wraps transient failures in InvalidOperationException; of those only
        // the deadlock victim is a write conflict.
        InvalidOperationException { InnerException: DbUpdateException update } => IsDeadlockVictim(update),
        _ => false
    };

    private static bool IsDeadlockVictim(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DeadlockVictim };
}
