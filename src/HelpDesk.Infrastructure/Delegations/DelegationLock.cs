using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Delegations;

internal static class DelegationLock
{
    // One IT team shares this transaction lock across scheduling, assignment and background processing.
    public static Task AcquireAsync(HelpDeskDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource=N'HelpDesk.DelegationAssignments',
            @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51000, 'Delegation processing is busy. Try again.', 1;
        """, ct);
}
