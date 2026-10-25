using HelpDesk.Application.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Attachments;

public sealed class AttachmentReconciler(IServiceScopeFactory scopes, IAttachmentStore store, ILogger<AttachmentReconciler> logger) : BackgroundService
{
    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        // Only old files are candidates; an in-flight upload must never be removed.
        var keys = store.KeysOlderThan(DateTimeOffset.UtcNow.AddDays(-1)).ToArray();
        if (keys.Length == 0)
            return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TicketAttachmentWriter>().RemoveUnreferencedAsync(keys, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning("Attachment reconciliation will retry ({ErrorType}).", exception.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
