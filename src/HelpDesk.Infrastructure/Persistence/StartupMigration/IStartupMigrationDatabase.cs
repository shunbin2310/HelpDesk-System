namespace HelpDesk.Infrastructure.Persistence.StartupMigration;

public interface IStartupMigrationDatabase
{
    string DatabaseName
    {
        get;
    }
    Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken);
    Task MigrateAsync(int commandTimeoutSeconds, CancellationToken cancellationToken);
}
