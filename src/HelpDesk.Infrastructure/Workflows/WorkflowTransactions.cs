using HelpDesk.Application.Workflows;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Workflows;

internal static class WorkflowTransactions
{
    public static async Task<WorkflowResult<T>> RunAsync<T>(HelpDeskDbContext db,
        Func<Task<WorkflowResult<T>>> operation, CancellationToken ct)
    {
        var outer = db.Database.CurrentTransaction;
        await using var owned = outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var transaction = outer ?? owned!;
        var savepoint = Guid.NewGuid().ToString("N");
        if (outer is not null)
            await transaction.CreateSavepointAsync(savepoint, ct);

        async Task Rollback()
        {
            if (outer is null)
                await transaction.RollbackAsync(CancellationToken.None);
            else
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            db.ChangeTracker.Clear();
        }

        try
        {
            var result = await operation();
            if (result.Status != WorkflowResultStatus.Success)
                await Rollback();
            else if (owned is not null)
                await owned.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await Rollback();
            return new(WorkflowResultStatus.Conflict, Error: "The Ticket or workflow changed. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await Rollback();
            return new(WorkflowResultStatus.Conflict, Error: "A workflow already exists or changed. Refresh and try again.");
        }
        catch
        {
            await Rollback();
            throw;
        }
    }
}
