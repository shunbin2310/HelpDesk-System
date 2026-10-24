using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Persistence.StartupMigration;

public sealed class StartupDatabaseMigrator(
    IStartupMigrationDatabase database,
    IOptions<DatabaseMigrationOptions> options,
    ILogger<StartupDatabaseMigrator> logger)
{
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.MigrateOnStartup)
        {
            logger.LogInformation("Startup database migration is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ExpectedDatabase) ||
            settings.CommandTimeoutSeconds is < 1 or > 1800)
            throw new InvalidOperationException("Startup migration requires ExpectedDatabase and a timeout between 1 and 1800 seconds.");

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!string.Equals(database.DatabaseName, settings.ExpectedDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected migration database.");

            var pending = await database.GetPendingAsync(cancellationToken);
            if (pending.Count == 0)
            {
                logger.LogInformation("No pending EF migrations; startup migration skipped.");
                return;
            }

            logger.LogInformation("Applying {Count} pending EF migrations: {Migrations}", pending.Count, string.Join(", ", pending));
            await database.MigrateAsync(settings.CommandTimeoutSeconds, cancellationToken);
            logger.LogInformation("Startup EF migration completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogCritical("Startup database migration failed; API startup is refused.");
            throw new InvalidOperationException("Startup database migration failed. Check database diagnostics; the API was not started.");
        }
    }
}
