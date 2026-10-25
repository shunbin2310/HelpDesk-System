using HelpDesk.Application.Tickets;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Tickets;

internal static class TicketWrites
{
    public static async Task<TicketResult<T>> RunAsync<T>(HelpDeskDbContext db, Func<Task<TicketResult<T>>> operation, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await operation();
            if (result.Status == TicketResultStatus.Success)
                await transaction.CommitAsync(ct);
            else
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
            return result;
        }
        catch (AttachmentValidationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return new(TicketResultStatus.Invalid, Error: exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return new(TicketResultStatus.Conflict, Error: "This Ticket changed. Refresh and try again.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
