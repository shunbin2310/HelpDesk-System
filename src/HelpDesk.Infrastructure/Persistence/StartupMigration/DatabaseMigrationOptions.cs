namespace HelpDesk.Infrastructure.Persistence.StartupMigration;

public sealed class DatabaseMigrationOptions
{
    public const string SectionName = "Database";
    public string? ExpectedDatabase
    {
        get; set;
    }
    public int CommandTimeoutSeconds { get; set; } = 180;
}
