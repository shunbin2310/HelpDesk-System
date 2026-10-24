namespace HelpDesk.IntegrationTests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION")))
            Skip = "Set HELPDESK_TEST_CONNECTION to an initialized, dedicated HelpDeskDb development/test database.";
    }
}

[CollectionDefinition("SQL Server", DisableParallelization = true)]
public sealed class SqlServerCollection;
