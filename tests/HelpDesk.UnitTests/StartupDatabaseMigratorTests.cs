using HelpDesk.Infrastructure.Persistence.StartupMigration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HelpDesk.UnitTests;

public sealed class StartupDatabaseMigratorTests
{
    private static StartupDatabaseMigrator Runner(FakeDatabase database, DatabaseMigrationOptions? options = null) =>
        new(database, Options.Create(options ?? new DatabaseMigrationOptions
        {
            ExpectedDatabase = "HelpDeskTest"
        }), NullLogger<StartupDatabaseMigrator>.Instance);

    [Fact]
    public async Task EveryStartupChecksPendingMigrations()
    {
        var database = new FakeDatabase();
        var runner = Runner(database);
        await runner.ApplyAsync();
        await runner.ApplyAsync();
        Assert.Equal(2, database.NameReads);
        Assert.Equal(2, database.PendingReads);
        Assert.Equal(0, database.Migrations);
    }

    [Fact]
    public async Task NoPendingSkipsMigration()
    {
        var database = new FakeDatabase();
        await Runner(database).ApplyAsync();
        Assert.Equal(1, database.PendingReads);
        Assert.Equal(0, database.Migrations);
    }

    [Fact]
    public async Task PendingAppliesOnceWithConfiguredTimeout()
    {
        var database = new FakeDatabase { Pending = ["InitialCreate"] };
        await Runner(database).ApplyAsync();
        Assert.Equal(1, database.Migrations);
        Assert.Equal(180, database.Timeout);
    }

    [Fact]
    public async Task WrongDatabaseIsRejectedBeforeReadingHistory()
    {
        var database = new FakeDatabase { Name = "AnotherDatabase" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(database).ApplyAsync());
        Assert.Equal(0, database.PendingReads);
        Assert.Equal(0, database.Migrations);
    }

    [Theory]
    [InlineData(null, 180)]
    [InlineData("", 180)]
    [InlineData("HelpDeskTest", 0)]
    [InlineData("HelpDeskTest", 1801)]
    public async Task InvalidOptionsAreRejectedBeforeDatabaseAccess(string? expectedDatabase, int timeout)
    {
        var database = new FakeDatabase();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(database, new()
        {
            ExpectedDatabase = expectedDatabase,
            CommandTimeoutSeconds = timeout
        }).ApplyAsync());
        Assert.Equal(0, database.NameReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationFailuresRefuseStartupWithoutLeakingDatabaseDetails(bool duringMigration)
    {
        var database = new FakeDatabase
        {
            Pending = ["InitialCreate"],
            FailQuery = !duringMigration,
            FailMigration = duringMigration
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(database).ApplyAsync());
        Assert.Contains("API was not started", error.Message);
        Assert.DoesNotContain("private database detail", error.ToString());
    }

    [Fact]
    public async Task CancellationDoesNotAccessDatabase()
    {
        var database = new FakeDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(database).ApplyAsync(cancellation.Token));
        Assert.Equal(0, database.NameReads);
        Assert.Equal(0, database.Migrations);
    }

    private sealed class FakeDatabase : IStartupMigrationDatabase
    {
        public string Name { get; init; } = "HelpDeskTest";
        public IReadOnlyList<string> Pending { get; init; } = [];
        public bool FailQuery
        {
            get; init;
        }
        public bool FailMigration
        {
            get; init;
        }
        public int NameReads
        {
            get; private set;
        }
        public int PendingReads
        {
            get; private set;
        }
        public int Migrations
        {
            get; private set;
        }
        public int Timeout
        {
            get; private set;
        }
        public string DatabaseName
        {
            get
            {
                NameReads++;
                return Name;
            }
        }

        public Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken)
        {
            PendingReads++;
            if (FailQuery)
                throw new InvalidOperationException("private database detail");
            return Task.FromResult(Pending);
        }

        public Task MigrateAsync(int commandTimeoutSeconds, CancellationToken cancellationToken)
        {
            Migrations++;
            Timeout = commandTimeoutSeconds;
            if (FailMigration)
                throw new InvalidOperationException("private database detail");
            return Task.CompletedTask;
        }
    }
}
