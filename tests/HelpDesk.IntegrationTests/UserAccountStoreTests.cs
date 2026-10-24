using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.DependencyInjection;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests;

[Collection("SQL Server")]
public sealed class UserAccountStoreTests
{
    [SqlServerFact]
    public async Task AccountCreationAndRoleReplacementKeepExactlyOneRoleAndRefreshSecurityStamp()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION")
        }).Build();
        await using var provider = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IUserAccountStore>();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var email = $"stage1-service-{Guid.NewGuid():N}@example.invalid";
        int? userId = null;
        try
        {
            var failed = await store.CreateAsync(new(email, "Stage 1 test", "", ApplicationRoles.Employee));
            Assert.False(failed.Succeeded);
            Assert.False(await db.Users.AnyAsync(x => x.Email == email));

            var created = await store.CreateAsync(new(email, "Stage 1 test", "Integration-only!42", ApplicationRoles.Employee));
            Assert.True(created.Succeeded, string.Join("; ", created.Errors));
            userId = created.UserId;
            db.ChangeTracker.Clear();
            var initialStamp = await db.Users.Where(x => x.Id == userId).Select(x => x.SecurityStamp).SingleAsync();
            var changed = await store.SetRoleAsync(userId!.Value, ApplicationRoles.SupportITStaff);
            Assert.True(changed.Succeeded, string.Join("; ", changed.Errors));
            db.ChangeTracker.Clear();
            Assert.Single(await db.UserRoles.Where(x => x.UserId == userId).ToListAsync());
            var role = await (from membership in db.UserRoles
                              join definition in db.Roles on membership.RoleId equals definition.Id
                              where membership.UserId == userId
                              select definition.Name).SingleAsync();
            Assert.Equal(ApplicationRoles.SupportITStaff, role);
            Assert.NotEqual(initialStamp, await db.Users.Where(x => x.Id == userId).Select(x => x.SecurityStamp).SingleAsync());

            var rejected = await store.SetRoleAsync(userId.Value, "UNKNOWN_ROLE");
            Assert.False(rejected.Succeeded);
            Assert.Single(await db.UserRoles.Where(x => x.UserId == userId).ToListAsync());
        }
        finally
        {
            db.ChangeTracker.Clear();
            if (userId.HasValue)
            {
                await using var cleanup = await db.Database.BeginTransactionAsync();
                await db.UserRoles.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.Users.Where(x => x.Id == userId && x.Email == email).ExecuteDeleteAsync();
                await cleanup.CommitAsync();
            }
        }
    }
}
