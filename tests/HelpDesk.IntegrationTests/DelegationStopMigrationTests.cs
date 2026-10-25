using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HelpDesk.IntegrationTests;

[Collection("SQL Server")]
public sealed class DelegationStopMigrationTests
{
    [SqlServerFact]
    public async Task ApiStartupAppliesStopAuditMigrationAndPreservesExistingData()
    {
        await using var test = new WorkflowTestDatabase();
        await using var db = test.Context();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261024051510_FixCurrentStepOwnership");
        var source = new ApplicationUser
        {
            FullName = "Original staff",
            UserName = "source@example.invalid",
            NormalizedUserName = "SOURCE@EXAMPLE.INVALID",
            Email = "source@example.invalid",
            NormalizedEmail = "SOURCE@EXAMPLE.INVALID"
        };
        var target = new ApplicationUser
        {
            FullName = "Delegate",
            UserName = "target@example.invalid",
            NormalizedUserName = "TARGET@EXAMPLE.INVALID",
            Email = "target@example.invalid",
            NormalizedEmail = "TARGET@EXAMPLE.INVALID"
        };
        db.Users.AddRange(source, target);
        await db.SaveChangesAsync();
        var starts = DateTimeOffset.UtcNow;
        var ends = starts.AddDays(1);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Delegations] ([SourceUserId], [TargetUserId], [StartsAtUtc], [EndsAtUtc],
                [CreatedByUserId], [CreatedAtUtc], [UpdatedAtUtc], [Reason])
            VALUES ({source.Id}, {target.Id}, {starts}, {ends}, {source.Id}, {starts}, {starts}, N'Existing schedule')
            """);
        db.Tickets.Add(new Ticket
        {
            TicketNumber = "UPGRADE-TEST",
            Title = "Existing ticket",
            Description = "Preserve during upgrade",
            Category = TicketCategory.Other,
            RequestedByUserId = source.Id
        });
        await db.SaveChangesAsync();
        using var client = test.Factory.CreateClient();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Contains("20261025083228_AddDelegationStopAudit", applied);
        var upgraded = await db.Delegations.SingleAsync();
        Assert.Equal(starts, upgraded.StartsAtUtc);
        Assert.Equal(ends, upgraded.EndsAtUtc);
        Assert.Equal("Existing schedule", upgraded.Reason);
        Assert.Null(upgraded.StoppedAtUtc);
        Assert.Null(upgraded.StoppedByUserId);
        Assert.Equal("Existing ticket", (await db.Tickets.SingleAsync()).Title);
        upgraded.StoppedAtUtc = starts;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        upgraded = await db.Delegations.SingleAsync();
        Assert.Null(upgraded.StoppedAtUtc);
        upgraded.StoppedAtUtc = starts;
        upgraded.StoppedByUserId = source.Id;
        await db.SaveChangesAsync();
        Assert.Equal(ends, upgraded.EndsAtUtc);

        await using var restarted = new HelpDeskTestFactory(db.Database.GetConnectionString()!);
        using var restartedClient = restarted.CreateClient();
        (await restartedClient.GetAsync("/health")).EnsureSuccessStatusCode();
        Assert.Equal(applied, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(1, await db.Delegations.CountAsync());
        Assert.Equal(1, await db.Tickets.CountAsync());
    }
}
