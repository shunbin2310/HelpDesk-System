using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistence.StartupMigration;

public sealed class EfStartupMigrationDatabase(HelpDeskDbContext context) : IStartupMigrationDatabase
{
    public string DatabaseName => context.Database.GetDbConnection().Database;

    public async Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken) =>
        (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

    public async Task MigrateAsync(int commandTimeoutSeconds, CancellationToken cancellationToken)
    {
        var previousTimeout = context.Database.GetCommandTimeout();
        try
        {
            context.Database.SetCommandTimeout(commandTimeoutSeconds);
            await context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            context.Database.SetCommandTimeout(previousTimeout);
        }
    }
}
