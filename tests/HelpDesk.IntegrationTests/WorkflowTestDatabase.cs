using System.Net.Http.Json;
using HelpDesk.Application.Authentication;
using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using HelpDesk.Infrastructure.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HelpDesk.IntegrationTests;

internal sealed class WorkflowTestDatabase : IAsyncDisposable
{
    private readonly string databaseName = $"HelpDeskWorkflowTests_{Guid.NewGuid():N}";
    private readonly string connection;
    public HelpDeskTestFactory Factory { get; }
    public Dictionary<string, int> Users { get; } = [];

    public WorkflowTestDatabase()
    {
        connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION"))
        { InitialCatalog = databaseName }.ConnectionString;
        Factory = new(connection);
    }

    public HelpDeskDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<HelpDeskDbContext>()
        .UseSqlServer(connection).AddInterceptors(interceptors).Options);

    public FileAttachmentStore Store() => new(Options.Create(new AttachmentStorageOptions { RootPath = Factory.AttachmentRoot }), NullLogger<FileAttachmentStore>.Instance);
    public TicketAttachmentWriter Files(HelpDeskDbContext db) => new(db, Store(), NullLogger<TicketAttachmentWriter>.Instance);

    public async Task InitializeAsync()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        foreach (var (name, role) in new[] { ("admin", ApplicationRoles.SystemAdmin), ("employee", ApplicationRoles.Employee),
                     ("other", ApplicationRoles.Employee), ("staff-a", ApplicationRoles.SupportITStaff),
                     ("staff-b", ApplicationRoles.SupportITStaff), ("support-admin", ApplicationRoles.SupportAdmin) })
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IUserAccountStore>()
                .CreateAsync(new($"{name}@example.invalid", name, "Integration-only!42", role));
            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            Users.Add(name, result.UserId!.Value);
        }
    }

    public async Task<HttpClient> ClientAsync(string name)
    {
        var client = Factory.CreateClient();
        var result = await client.PostAsJsonAsync("/auth/login", new { email = $"{name}@example.invalid", password = "Integration-only!42" });
        result.EnsureSuccessStatusCode();
        var login = (await result.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        return client;
    }

    public async Task<long> AddTicketAsync()
    {
        await using var db = Context();
        var ticket = new Ticket { TicketNumber = $"T-{Guid.NewGuid():N}"[..30], Title = "Workflow integration test",
            Description = "Disposable test data", Category = TicketCategory.Software, RequestedByUserId = Users["employee"] };
        db.Tickets.Add(ticket); await db.SaveChangesAsync(); return ticket.Id;
    }

    public async Task AssignAsync(long ticketId, string name)
    {
        await using var db = Context();
        var ticket = await db.Tickets.SingleAsync(x => x.Id == ticketId);
        ticket.AssignedToUserId = Users[name]; ticket.AssignmentVersion++; await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = Context())
        {
            Assert.Equal(databaseName, db.Database.GetDbConnection().Database);
            Assert.StartsWith("HelpDeskWorkflowTests_", databaseName);
            await db.Database.EnsureDeletedAsync();
        }
        await Factory.DisposeAsync();
    }
}

internal sealed record TestCurrentUser(int UserId) : ICurrentUserContext;
