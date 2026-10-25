using System.Net;
using System.Net.Http.Json;
using System.Text;
using HelpDesk.Application.Tickets;
using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Attachments;
using HelpDesk.Infrastructure.Tickets;
using HelpDesk.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.IntegrationTests;

[Collection("SQL Server")]
public sealed class TicketAttachmentTests
{
    [SqlServerFact]
    public async Task SubmissionAttachmentsFollowCurrentPermissionsAndRetiredMessagesStayPrivate()
    {
        await using var test = new WorkflowTestDatabase();
        await test.InitializeAsync();
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        using var support = await test.ClientAsync("support-admin");
        using var a = await test.ClientAsync("staff-a");
        using var b = await test.ClientAsync("staff-b");
        using var other = await test.ClientAsync("other");
        using var form = Form(("evidence.txt", "Initial evidence"));
        var ticket = await Detail(await employee.PostAsync("/tickets", form));
        var id = ticket.Ticket.Id;
        ticket = await Assign(support, ticket, test.Users["staff-a"]);
        var legacyFiles = new List<long>();
        await using (var db = test.Context())
        {
            foreach (var type in new[] { TicketMessageType.PublicReply, TicketMessageType.InternalNote })
            {
                var message = new TicketMessage { TicketId = id, AuthorUserId = test.Users["staff-a"], MessageType = type, Body = "Legacy message" };
                db.TicketMessages.Add(message);
                await db.SaveChangesAsync();
                var file = await test.Store().SaveAsync(new("legacy.txt", 6, () => new MemoryStream("legacy"u8.ToArray())));
                var attachment = new Attachment
                {
                    TicketId = id,
                    TicketMessageId = message.Id,
                    UploadedByUserId = test.Users["staff-a"],
                    OriginalFileName = file.FileName,
                    StorageKey = file.StorageKey,
                    SizeBytes = file.SizeBytes,
                    ContentType = file.ContentType
                };
                db.Attachments.Add(attachment);
                await db.SaveChangesAsync();
                legacyFiles.Add(attachment.Id);
            }
        }
        var files = (await employee.GetFromJsonAsync<AttachmentItem[]>($"/tickets/{id}/attachments"))!;
        var initial = Assert.Single(files);
        Assert.Equal("evidence.txt", initial.FileName);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/tickets/{id}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Url(id, initial.Id))).StatusCode);
        foreach (var allowed in new[] { employee, a, admin, support })
        {
            var download = await allowed.GetAsync(Url(id, initial.Id));
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("Initial evidence", await download.Content.ReadAsStringAsync());
            Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
            Assert.Contains("attachment", download.Content.Headers.ContentDisposition!.ToString());
            Assert.DoesNotContain("storageKey", await allowed.GetStringAsync($"/tickets/{id}/attachments"));
            Assert.Equal(HttpStatusCode.NotFound, (await allowed.GetAsync($"/tickets/{id}/conversation")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await allowed.PostAsJsonAsync($"/tickets/{id}/messages", new
            {
                body = "Retired",
                messageType = 1
            })).StatusCode);
            foreach (var fileId in legacyFiles)
                Assert.Equal(HttpStatusCode.NotFound, (await allowed.GetAsync(Url(id, fileId))).StatusCode);
        }
        ticket = await Assign(support, ticket, test.Users["staff-b"]);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/tickets/{id}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync(Url(id, initial.Id))).StatusCode);
        Assert.Single((await b.GetFromJsonAsync<AttachmentItem[]>($"/tickets/{id}/attachments"))!);
        Assert.Equal("Initial evidence", await b.GetStringAsync(Url(id, initial.Id)));
        var second = await Detail(await employee.PostAsJsonAsync("/tickets", new CreateTicketRequest("Other", "Details", TicketCategory.Other)));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(Url(second.Ticket.Id, initial.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.DeleteAsync(Url(id, initial.Id))).StatusCode);
        await using var verify = test.Context();
        Assert.Equal(2, await verify.TicketMessages.CountAsync());
        Assert.Equal(3, await verify.Attachments.CountAsync());
    }

    [SqlServerFact]
    public async Task AttachmentsRemainReadableAfterClosureAndCannotBeAppendedOrEdited()
    {
        await using var test = new WorkflowTestDatabase();
        await test.InitializeAsync();
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        using var staff = await test.ClientAsync("staff-a");
        using var form = Form(("readme.txt", "Keep readable"));
        var ticket = await Assign(admin, await Detail(await employee.PostAsync("/tickets", form)), test.Users["staff-a"]);
        var id = ticket.Ticket.Id;
        var file = Assert.Single((await employee.GetFromJsonAsync<AttachmentItem[]>($"/tickets/{id}/attachments"))!);
        await Action(staff, ticket, TicketWorkflow.StartProcessing);
        ticket = await Read(staff, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/tickets/{id}/workflow/actions", new ExecuteWorkflowAction(TicketWorkflow.Resolve, ticket.Workflow.RowVersion))).StatusCode);
        await Action(staff, ticket, TicketWorkflow.Resolve);
        ticket = await Read(employee, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync($"/tickets/{id}/workflow/actions", new ExecuteWorkflowAction(TicketWorkflow.Reopen, ticket.Workflow.RowVersion))).StatusCode);
        await Action(employee, ticket, TicketWorkflow.Close);
        foreach (var client in new[] { employee, staff, admin })
        {
            Assert.Single((await client.GetFromJsonAsync<AttachmentItem[]>($"/tickets/{id}/attachments"))!);
            Assert.Equal("Keep readable", await client.GetStringAsync(Url(id, file.Id)));
            using var extra = Form(("late.txt", "Late"));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync($"/tickets/{id}/attachments", extra)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync(Url(id, file.Id), new
            {
                fileName = "changed.txt"
            })).StatusCode);
        }
        await using var verify = test.Context();
        Assert.Empty(await verify.TicketMessages.ToListAsync());
        Assert.Single(await verify.Attachments.ToListAsync());
    }

    [SqlServerFact]
    public async Task InvalidBatchesRollbackTheWholeTicketAndFiveFilesCanBeSubmittedTogether()
    {
        await using var test = new WorkflowTestDatabase();
        await test.InitializeAsync();
        await Setup(test);
        using var employee = await test.ClientAsync("employee");
        using (var bad = Form(("good.txt", "Valid"), ("fake.pdf", "Invalid PDF")))
            Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsync("/tickets", bad)).StatusCode);
        using (var six = Form(Enumerable.Range(0, 6).Select(i => ($"{i}.txt", "Test")).ToArray()))
            Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsync("/tickets", six)).StatusCode);
        using (var invalid = Form(("good.txt", "Valid"), ("bad.docx", "Invalid ZIP")))
            Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsync("/tickets", invalid)).StatusCode);
        using (var oversized = Form())
        {
            oversized.Add(new ByteArrayContent(new byte[AttachmentLimits.MaxFileBytes + 1]), "files", "big.txt");
            Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsync("/tickets", oversized)).StatusCode);
        }
        await using (var db = test.Context())
        {
            Assert.Empty(await db.Tickets.ToListAsync());
            Assert.Empty(await db.WorkflowProcessInstances.ToListAsync());
            Assert.Empty(await db.WorkflowHistory.ToListAsync());
            Assert.Empty(await db.Attachments.ToListAsync());
        }
        Assert.Empty(Directory.GetFiles(test.Factory.AttachmentRoot));
        using var five = Form(Enumerable.Range(0, 5).Select(i => ($"{i}.txt", "Test")).ToArray());
        var ticket = await Detail(await employee.PostAsync("/tickets", five));
        Assert.Equal(5, (await employee.GetFromJsonAsync<AttachmentItem[]>($"/tickets/{ticket.Ticket.Id}/attachments"))!.Length);
        await using var verify = test.Context();
        Assert.Single(await verify.Tickets.ToListAsync());
        Assert.Equal(5, await verify.Attachments.CountAsync());
        Assert.Equal(5, Directory.GetFiles(test.Factory.AttachmentRoot).Length);
    }

    [SqlServerFact]
    public async Task DatabaseFailuresCleanFilesAndReconciliationPreservesCommittedAndRecentFiles()
    {
        await using var test = new WorkflowTestDatabase();
        await test.InitializeAsync();
        await Setup(test);
        using var employee = await test.ClientAsync("employee");
        var upload = new AttachmentUpload("x.txt", 5, () => new MemoryStream("hello"u8.ToArray()));
        await using (var db = test.Context(new FailAttachmentWrite()))
        {
            var current = new TestCurrentUser(test.Users["employee"]);
            var create = new TicketService(db, current, new WorkflowEngine(db, current), test.Files(db), test.Processor(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => create.CreateAsync(new("Fail", "Rollback", TicketCategory.Other), [upload]));
        }
        await using (var db = test.Context())
        {
            Assert.Empty(await db.Tickets.ToListAsync());
            Assert.Empty(await db.WorkflowProcessInstances.ToListAsync());
            Assert.Empty(await db.Attachments.ToListAsync());
        }
        Assert.Empty(Directory.GetFiles(test.Factory.AttachmentRoot));
        using var form = Form(("persist.txt", "Keep"));
        _ = await Detail(await employee.PostAsync("/tickets", form));
        var store = test.Store();
        var orphan = await store.SaveAsync(upload);
        var recent = await store.SaveAsync(upload);
        foreach (var path in Directory.GetFiles(test.Factory.AttachmentRoot).Where(x => !x.EndsWith(recent.StorageKey + ".blob", StringComparison.Ordinal)))
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        await new AttachmentReconciler(test.Factory.Services.GetRequiredService<IServiceScopeFactory>(), store, NullLogger<AttachmentReconciler>.Instance).ReconcileAsync();
        Assert.False(File.Exists(Path.Combine(test.Factory.AttachmentRoot, orphan.StorageKey + ".blob")));
        Assert.True(File.Exists(Path.Combine(test.Factory.AttachmentRoot, recent.StorageKey + ".blob")));
        await using var verify = test.Context();
        var key = await verify.Attachments.Select(x => x.StorageKey).SingleAsync();
        await test.Files(verify).RemoveUnreferencedAsync([key]);
        using var readable = store.OpenRead(key);
        Assert.Equal(4, readable.Length);
    }

    private static MultipartFormDataContent Form(params (string Name, string Content)[] files)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("Attachment test"), "title");
        form.Add(new StringContent("Issue details"), "description");
        form.Add(new StringContent("1"), "category");
        foreach (var file in files)
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(file.Content)), "files", file.Name);
        return form;
    }
    private static async Task Setup(WorkflowTestDatabase test)
    {
        using var admin = await test.ClientAsync("admin");
        (await admin.PostAsJsonAsync("/workflow-templates/default", new
        {
        })).EnsureSuccessStatusCode();
    }
    private static string Url(long ticket, long file) => $"/tickets/{ticket}/attachments/{file}/download";
    private static async Task<TicketDetail> Detail(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TicketDetail>())!;
    }
    private static async Task<TicketDetail> Read(HttpClient client, long id) => (await client.GetFromJsonAsync<TicketDetail>($"/tickets/{id}"))!;
    private static async Task<TicketDetail> Assign(HttpClient client, TicketDetail ticket, int staff)
    {
        var result = await client.PutAsJsonAsync($"/tickets/{ticket.Ticket.Id}/assignment", new AssignTicketRequest(staff, ticket.RowVersion));
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<TicketDetail>())!;
    }
    private static async Task Action(HttpClient client, TicketDetail ticket, string action) =>
        (await client.PostAsJsonAsync($"/tickets/{ticket.Ticket.Id}/workflow/actions", new ExecuteWorkflowAction(action, ticket.Workflow.RowVersion, "Verified by test"))).EnsureSuccessStatusCode();
    private sealed class FailAttachmentWrite : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Attachment>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Injected attachment failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
