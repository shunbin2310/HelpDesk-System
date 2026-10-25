using System.Net;
using System.Net.Http.Json;
using HelpDesk.Application.Authentication;
using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests;

[Collection("SQL Server")]
public sealed class AuthenticationTests
{
    [SqlServerFact]
    public async Task AccountLifecycleEnforcesAuthorizationSessionRevocationAndLastAdminProtection()
    {
        // Isolate bootstrap and concurrency tests from every real account in the configured database.
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION"))
        {
            InitialCatalog = $"HelpDeskAuthTests_{Guid.NewGuid():N}"
        };
        var factory = new HelpDeskTestFactory(connection.ConnectionString);
        try
        {
            using var client = factory.CreateClient();
            async Task<UserAccountResult> Bootstrap(string email)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<IUserAccountStore>()
                    .BootstrapAdminAsync(new(email, "Test Admin", "Integration-only!42", ApplicationRoles.SystemAdmin));
            }
            var initial = await Task.WhenAll(Bootstrap("first@example.invalid"), Bootstrap("second@example.invalid"));
            Assert.Single(initial, x => x.Succeeded);
            Assert.Single(initial, x => !x.Succeeded);
            var adminEmail = initial[0].Succeeded ? "first@example.invalid" : "second@example.invalid";
            var adminId = initial.Single(x => x.Succeeded).UserId!.Value;

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/users")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/auth/bootstrap-admin", new
            {
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/login", new
            {
                email = adminEmail,
                password = "bad"
            })).StatusCode);
            var admin = await Login(client, adminEmail);
            client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
            var me = await client.GetFromJsonAsync<UserAccount>("/auth/me");
            Assert.Equal(ApplicationRoles.SystemAdmin, me!.Role);
            var firstPage = await client.GetFromJsonAsync<UserAccountPage>("/users?page=0");
            Assert.Equal(1, firstPage!.Page);
            Assert.Equal(adminId, Assert.Single(firstPage.Items).Id);
            Assert.Equal(1, firstPage.Total);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/users/{adminId}/active", new
            {
            })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/users/{adminId}/active", new
            {
                isActive = false
            })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/users/{adminId}/role", new
            {
                role = ApplicationRoles.Employee
            })).StatusCode);
            var created = await client.PostAsJsonAsync("/users", new CreateUserAccount("employee@example.invalid", "Employee", "Integration-only!42", ApplicationRoles.Employee));
            created.EnsureSuccessStatusCode();
            var employeeId = (await created.Content.ReadFromJsonAsync<Dictionary<string, int>>())!["userId"];
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/users", new CreateUserAccount("EMPLOYEE@example.invalid", "Duplicate", "Integration-only!42", ApplicationRoles.Employee))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/users", new
            {
                email = "null@example.invalid",
                fullName = (string?)null,
                password = (string?)null,
                role = "EMPLOYEE"
            })).StatusCode);

            using var employeeClient = factory.CreateClient();
            var employee = await Login(employeeClient, "employee@example.invalid");
            employeeClient.DefaultRequestHeaders.Authorization = new("Bearer", employee.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync("/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.PutAsJsonAsync($"/users/{employeeId}/role", new
            {
                role = ApplicationRoles.SystemAdmin
            })).StatusCode);
            (await client.PutAsJsonAsync($"/users/{employeeId}/role", new
            {
                role = ApplicationRoles.SupportITStaff
            })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await employeeClient.GetAsync("/auth/me")).StatusCode);
            var staff = await Login(employeeClient, "employee@example.invalid");
            Assert.Equal(ApplicationRoles.SupportITStaff, staff.User.Role);
            employeeClient.DefaultRequestHeaders.Authorization = new("Bearer", staff.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync("/users")).StatusCode);
            (await client.PutAsJsonAsync($"/users/{employeeId}/active", new
            {
                isActive = false
            })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await employeeClient.GetAsync("/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await employeeClient.PostAsJsonAsync("/auth/login", new
            {
                email = "employee@example.invalid",
                password = "Integration-only!42"
            })).StatusCode);
            (await client.PutAsJsonAsync($"/users/{employeeId}/active", new
            {
                isActive = true
            })).EnsureSuccessStatusCode();
            // Reactivation must not revive a token issued before disable.
            Assert.Equal(HttpStatusCode.Unauthorized, (await employeeClient.GetAsync("/auth/me")).StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                var fresh = await auth.LoginAsync(new("employee@example.invalid", "Integration-only!42"));
                employeeClient.DefaultRequestHeaders.Authorization = new("Bearer", fresh!.AccessToken);
            }
            (await client.PostAsJsonAsync($"/users/{employeeId}/reset-password", new
            {
                password = "Reset-integration!43"
            })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await employeeClient.GetAsync("/auth/me")).StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                Assert.Null(await auth.LoginAsync(new("employee@example.invalid", "Integration-only!42")));
                var reset = await auth.LoginAsync(new("employee@example.invalid", "Reset-integration!43"));
                employeeClient.DefaultRequestHeaders.Authorization = new("Bearer", reset!.AccessToken);
            }
            (await employeeClient.PostAsJsonAsync("/auth/change-password", new
            {
                currentPassword = "Reset-integration!43",
                newPassword = "Changed-integration!44"
            })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await employeeClient.GetAsync("/auth/me")).StatusCode);

            // Two simultaneous administrator demotions must leave exactly one active administrator.
            var secondAdmin = await client.PostAsJsonAsync("/users", new CreateUserAccount("another@example.invalid", "Another Admin", "Integration-only!42", ApplicationRoles.SystemAdmin));
            secondAdmin.EnsureSuccessStatusCode();
            var secondId = (await secondAdmin.Content.ReadFromJsonAsync<Dictionary<string, int>>())!["userId"];
            var demotions = await Task.WhenAll(Demote(factory, adminId), Demote(factory, secondId));
            Assert.Single(demotions, x => x.Succeeded);
            Assert.Single(demotions, x => !x.Succeeded);
            using var supportAdminClient = factory.CreateClient();
            var demotedEmail = demotions[0].Succeeded ? adminEmail : "another@example.invalid";
            var supportAdmin = await Login(supportAdminClient, demotedEmail);
            Assert.Equal(ApplicationRoles.SupportAdmin, supportAdmin.User.Role);
            supportAdminClient.DefaultRequestHeaders.Authorization = new("Bearer", supportAdmin.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await supportAdminClient.GetAsync("/users")).StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                // Password failures lock the account; a correct password does not bypass the lock.
                for (var i = 0; i < 5; i++)
                    Assert.Null(await auth.LoginAsync(new("employee@example.invalid", "wrong")));
                Assert.Null(await auth.LoginAsync(new("employee@example.invalid", "Changed-integration!44")));
                Assert.True(await manager.IsLockedOutAsync((await manager.FindByIdAsync(employeeId.ToString()))!));
            }
            var throttled = false;
            for (var i = 0; i < 11; i++)
                throttled |= (await supportAdminClient.PostAsJsonAsync("/auth/login", new
                {
                    email = "unknown@example.invalid",
                    password = "wrong"
                })).StatusCode == HttpStatusCode.TooManyRequests;
            Assert.True(throttled);
        }
        finally
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
                Assert.Equal(connection.InitialCatalog, db.Database.GetDbConnection().Database);
                Assert.StartsWith("HelpDeskAuthTests_", connection.InitialCatalog);
                await db.Database.EnsureDeletedAsync();
            }
            await factory.DisposeAsync();
        }
    }

    private static async Task<LoginResponse> Login(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = "Integration-only!42"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static async Task<UserAccountResult> Demote(HelpDeskTestFactory factory, int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserAccountStore>().SetRoleAsync(id, ApplicationRoles.SupportAdmin);
    }

}
