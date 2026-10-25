using System.Net;
using System.Net.Http.Json;
using HelpDesk.Application.Tickets;
using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Tickets;
using HelpDesk.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HelpDesk.IntegrationTests;

[Collection("SQL Server")]
public sealed class TicketTests
{
    [SqlServerFact]
    public async Task EmployeeSubmissionAssignmentAndWorkflowFormACompletePublicApiLifecycle()
    {
        await using var test = new WorkflowTestDatabase(); await test.InitializeAsync(); await Setup(test);
        using var employee = await test.ClientAsync("employee"); using var admin = await test.ClientAsync("admin");
        using var support = await test.ClientAsync("support-admin"); using var staff = await test.ClientAsync("staff-a");
        using var other = await test.ClientAsync("other"); using var anonymous = test.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/tickets")).StatusCode);
        foreach (var forbidden in new[] { admin, support, staff })
            Assert.Equal(HttpStatusCode.Forbidden, (await forbidden.PostAsJsonAsync("/tickets", new CreateTicketRequest("No", "No", TicketCategory.Other))).StatusCode);
        var response = await employee.PostAsJsonAsync("/tickets", new { title = "  Cannot connect to VPN  ", description = "  Connection drops every minute.  ",
            category = 3, requestedByUserId = test.Users["other"], assignedToUserId = test.Users["staff-a"], priority = 4, status = "CLOSED" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = (await response.Content.ReadFromJsonAsync<TicketDetail>())!;
        var id = ticket.Ticket.Id;
        Assert.Equal(TicketNumbers.Create(id, ticket.Ticket.CreatedAtUtc), ticket.Ticket.TicketNumber);
        Assert.Equal("Cannot connect to VPN", ticket.Ticket.Title); Assert.Equal("Connection drops every minute.", ticket.Description);
        Assert.Equal(test.Users["employee"], ticket.Ticket.RequestedByUserId); Assert.Equal("Medium", ticket.Ticket.Priority);
        Assert.Equal("OPEN", ticket.Ticket.Status); Assert.Null(ticket.Ticket.AssignedToUserId); Assert.Single(ticket.Workflow.History);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/tickets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/tickets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/tickets/assignees")).StatusCode);
        var assignees = await support.GetFromJsonAsync<TicketAssignee[]>("/tickets/assignees"); Assert.Equal(2, assignees!.Length);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync($"/tickets/{id}/assignment", new AssignTicketRequest(test.Users["staff-a"], ticket.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await support.PutAsJsonAsync($"/tickets/{id}/assignment", new AssignTicketRequest(test.Users["employee"], ticket.RowVersion))).StatusCode);
        ticket = await Detail(await support.PutAsJsonAsync($"/tickets/{id}/assignment", new AssignTicketRequest(test.Users["staff-a"], ticket.RowVersion)));
        Assert.Equal("OPEN", ticket.Ticket.Status); Assert.Single(ticket.ManagementHistory); Assert.Equal("Assigned", ticket.ManagementHistory[0].EventType);
        Assert.Single((await Page(staff)).Items); Assert.Empty((await Page(other)).Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"/tickets/{id}/priority", new SetTicketPriorityRequest(TicketPriority.High, ticket.RowVersion))).StatusCode);
        ticket = await Detail(await support.PutAsJsonAsync($"/tickets/{id}/priority", new SetTicketPriorityRequest(TicketPriority.High, ticket.RowVersion)));
        Assert.Equal("High", ticket.Ticket.Priority); Assert.Equal("PriorityChanged", ticket.ManagementHistory.Last().EventType);
        var progress = await Workflow(await Act(staff, ticket, TicketWorkflow.StartProcessing));
        ticket = await Read(staff, id); Assert.Equal("IN_PROGRESS", ticket.Ticket.Status); Assert.Equal(progress.RowVersion, ticket.Workflow.RowVersion);
        Assert.Single((await Page(support, "IN_PROGRESS")).Items); Assert.Empty((await Page(employee, "OPEN")).Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(support, ticket, TicketWorkflow.Resolve)).StatusCode);
        _ = await Workflow(await Act(staff, ticket, TicketWorkflow.Resolve)); ticket = await Read(employee, id);
        _ = await Workflow(await Act(employee, ticket, TicketWorkflow.Reopen)); ticket = await Read(staff, id);
        _ = await Workflow(await Act(staff, ticket, TicketWorkflow.Resolve)); ticket = await Read(employee, id);
        _ = await Workflow(await Act(employee, ticket, TicketWorkflow.Close)); ticket = await Read(employee, id);
        Assert.Equal("CLOSED", ticket.Ticket.Status); Assert.Equal("Completed", ticket.Workflow.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(admin, ticket, TicketWorkflow.Reopen)).StatusCode);
        Assert.Single((await Page(admin, "CLOSED")).Items);
    }

    [SqlServerFact]
    public async Task ReassignmentRevokesEveryOldStaffRouteAndConcurrentAdminEditsRejectStaleVersions()
    {
        await using var test = new WorkflowTestDatabase(); await test.InitializeAsync(); await Setup(test);
        using var employee = await test.ClientAsync("employee"); using var support = await test.ClientAsync("support-admin");
        using var staffA = await test.ClientAsync("staff-a"); using var staffB = await test.ClientAsync("staff-b");
        var ticket = await Detail(await employee.PostAsJsonAsync("/tickets", new CreateTicketRequest("Printer", "Not printing", TicketCategory.Hardware)));
        var id = ticket.Ticket.Id; var before = ticket.RowVersion;
        ticket = await Detail(await support.PutAsJsonAsync($"/tickets/{id}/assignment", new AssignTicketRequest(test.Users["staff-a"], before)));
        Assert.Equal(HttpStatusCode.Conflict, (await support.PutAsJsonAsync($"/tickets/{id}/priority", new SetTicketPriorityRequest(TicketPriority.High, before))).StatusCode);
        ticket = await Detail(await support.PutAsJsonAsync($"/tickets/{id}/assignment", new AssignTicketRequest(test.Users["staff-a"], ticket.RowVersion)));
        Assert.Single(ticket.ManagementHistory);
        _ = await Workflow(await Act(staffA, ticket, TicketWorkflow.StartProcessing)); ticket = await Read(support, id);
        var priorWorkflow = ticket.Workflow.RowVersion;
        ticket = await Detail(await support.PutAsJsonAsync($"/tickets/{id}/assignment", new AssignTicketRequest(test.Users["staff-b"], ticket.RowVersion)));
        Assert.Equal(priorWorkflow, ticket.Workflow.RowVersion); Assert.Equal("IN_PROGRESS", ticket.Ticket.Status);
        Assert.Empty((await Page(staffA)).Items); Assert.Single((await Page(staffB)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await staffA.GetAsync($"/tickets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staffA.GetAsync($"/tickets/{id}/workflow")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(staffA, ticket, TicketWorkflow.Resolve)).StatusCode);
        var edits = await Task.WhenAll(support.PutAsJsonAsync($"/tickets/{id}/priority", new SetTicketPriorityRequest(TicketPriority.High, ticket.RowVersion)),
            support.PutAsJsonAsync($"/tickets/{id}/priority", new SetTicketPriorityRequest(TicketPriority.Critical, ticket.RowVersion)));
        Assert.Single(edits, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(edits, x => x.StatusCode == HttpStatusCode.Conflict);
        ticket = await Read(staffB, id); Assert.Equal(3, ticket.ManagementHistory.Count);
        Assert.Equal("staff-a", ticket.ManagementHistory[1].OldAssigneeName); Assert.Equal("staff-b", ticket.ManagementHistory[1].NewAssigneeName);
        _ = await Workflow(await Act(staffB, ticket, TicketWorkflow.Resolve));
        await using var db = test.Context(); Assert.Equal(2, (await db.Tickets.SingleAsync()).AssignmentVersion);
        var inactive = await db.Users.SingleAsync(x => x.Id == test.Users["staff-a"]); inactive.IsActive = false; await db.SaveChangesAsync();
        ticket = await Read(support, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await support.PutAsJsonAsync($"/tickets/{id}/assignment", new AssignTicketRequest(inactive.Id, ticket.RowVersion))).StatusCode);
        Assert.Single((await support.GetFromJsonAsync<TicketAssignee[]>("/tickets/assignees"))!);
    }

    [SqlServerFact]
    public async Task PaginationAndFiltersRespectTheVisibilityOfEachRole()
    {
        await using var test = new WorkflowTestDatabase(); await test.InitializeAsync(); await Setup(test);
        using var employee = await test.ClientAsync("employee"); using var other = await test.ClientAsync("other");
        using var admin = await test.ClientAsync("admin"); using var staff = await test.ClientAsync("staff-a");
        for (var i = 0; i < 26; i++) (await employee.PostAsJsonAsync("/tickets", new CreateTicketRequest($"Issue {i}", "Detail", TicketCategory.Other))).EnsureSuccessStatusCode();
        var ownOther = await Detail(await other.PostAsJsonAsync("/tickets", new CreateTicketRequest("Other employee", "Detail", TicketCategory.Software)));
        var first = await Page(employee); var second = (await employee.GetFromJsonAsync<TicketPage>("/tickets?page=2"))!;
        Assert.Equal(26, first.Total); Assert.Equal(25, first.Items.Count); Assert.Single(second.Items);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.Equal(27, (await Page(admin)).Total); Assert.Single((await Page(other)).Items); Assert.Empty((await Page(staff)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/tickets/{ownOther.Ticket.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.GetAsync("/tickets?status=PENDING")).StatusCode);
        Assert.Equal(27, (await admin.GetFromJsonAsync<TicketPage>("/tickets?onlyUnassigned=true"))!.Total);
    }

    [SqlServerFact]
    public async Task MissingOrInvalidTemplatesAndInjectedHistoryFailuresRollbackTheWholeNewTicket()
    {
        await using var test = new WorkflowTestDatabase(); await test.InitializeAsync();
        using var employee = await test.ClientAsync("employee");
        Assert.Equal(HttpStatusCode.Conflict, (await employee.PostAsJsonAsync("/tickets", new CreateTicketRequest("No template", "Detail", TicketCategory.Other))).StatusCode);
        await using (var db = test.Context()) Assert.False(await db.Tickets.AnyAsync());
        await Setup(test);
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync("/tickets", new { title = " ", description = "", category = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync("/tickets", new { title = new string('a', 201), description = "Detail", category = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync("/tickets", new { title = "Test", description = (string?)null, category = 1 })).StatusCode);
        await using (var db = test.Context(new HistoryFailure()))
        {
            var current = new TestCurrentUser(test.Users["employee"]);
            var service = new TicketService(db, current, new WorkflowEngine(db, current), test.Files(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new("Failure", "Rollback everything", TicketCategory.Other)));
        }
        await using (var db = test.Context())
        {
            Assert.False(await db.Tickets.AnyAsync()); Assert.False(await db.WorkflowProcessInstances.AnyAsync());
            Assert.False(await db.WorkflowStepInstances.AnyAsync()); Assert.False(await db.WorkflowActionInstances.AnyAsync());
            Assert.False(await db.WorkflowActionerInstances.AnyAsync()); Assert.False(await db.WorkflowHistory.AnyAsync());
            var action = await db.WorkflowActionTemplates.FirstAsync(x => x.Code == TicketWorkflow.StartProcessing);
            action.ToStepTemplateId = await db.WorkflowStepTemplates.Where(x => x.Code == TicketWorkflow.Closed).Select(x => x.Id).SingleAsync();
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await employee.PostAsJsonAsync("/tickets", new CreateTicketRequest("Invalid template", "Detail", TicketCategory.Other))).StatusCode);
        await using var verify = test.Context(); Assert.False(await verify.Tickets.AnyAsync());
    }

    private static async Task Setup(WorkflowTestDatabase test)
    { using var admin = await test.ClientAsync("admin"); (await admin.PostAsJsonAsync("/workflow-templates/default", new { })).EnsureSuccessStatusCode(); }
    private static async Task<TicketDetail> Detail(HttpResponseMessage response)
    { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<TicketDetail>())!; }
    private static async Task<WorkflowSnapshot> Workflow(HttpResponseMessage response)
    { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<WorkflowSnapshot>())!; }
    private static async Task<TicketPage> Page(HttpClient client, string? status = null) =>
        (await client.GetFromJsonAsync<TicketPage>("/tickets" + (status is null ? "" : $"?status={status}")))!;
    private static async Task<TicketDetail> Read(HttpClient client, long id) => (await client.GetFromJsonAsync<TicketDetail>($"/tickets/{id}"))!;
    private static Task<HttpResponseMessage> Act(HttpClient client, TicketDetail detail, string code) =>
        client.PostAsJsonAsync($"/tickets/{detail.Ticket.Id}/workflow/actions", new ExecuteWorkflowAction(code, detail.Workflow.RowVersion,
            code is TicketWorkflow.Resolve or TicketWorkflow.Reopen ? "Integration test comment" : null));

    private sealed class HistoryFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<WorkflowHistory>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Injected history failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
