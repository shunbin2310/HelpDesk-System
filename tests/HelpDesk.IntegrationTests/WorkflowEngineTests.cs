using System.Net;
using System.Net.Http.Json;
using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HelpDesk.IntegrationTests;

[Collection("SQL Server")]
public sealed class WorkflowEngineTests
{
    [SqlServerFact]
    public async Task DefaultSetupAndTicketLifecycleEnforceHttpPermissionsAndRecordEveryTransition()
    {
        await using var test = new WorkflowTestDatabase();
        await test.InitializeAsync();
        using var anonymous = test.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/workflow-templates/active")).StatusCode);
        using var admin = await test.ClientAsync("admin");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/workflow-templates/active")).StatusCode);
        var setups = await Task.WhenAll(admin.PostAsJsonAsync("/workflow-templates/default", new
        {
        }), admin.PostAsJsonAsync("/workflow-templates/default", new
        {
        }));
        foreach (var setup in setups)
            setup.EnsureSuccessStatusCode();
        Assert.Equal((await setups[0].Content.ReadFromJsonAsync<WorkflowTemplateSnapshot>())!.Id,
            (await setups[1].Content.ReadFromJsonAsync<WorkflowTemplateSnapshot>())!.Id);
        using var employee = await test.ClientAsync("employee");
        using var other = await test.ClientAsync("other");
        using var staff = await test.ClientAsync("staff-a");
        using var supportAdmin = await test.ClientAsync("support-admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/workflow-templates/default", new
        {
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supportAdmin.GetAsync("/workflow-templates/active")).StatusCode);

        var ticket = await test.AddTicketAsync();
        await using (var db = test.Context())
        {
            Assert.Equal(WorkflowResultStatus.Forbidden, (await Engine(db, test, "admin").StartAsync(ticket)).Status);
            Assert.Equal(WorkflowResultStatus.Success, (await Engine(db, test, "employee").StartAsync(ticket)).Status);
            Assert.Equal(WorkflowResultStatus.Conflict, (await Engine(db, test, "employee").StartAsync(ticket)).Status);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/tickets/{ticket}/workflow")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/tickets/{ticket}/workflow")).StatusCode);
        var open = await Read(employee, ticket);
        Assert.Equal(TicketWorkflow.Open, open.CurrentStepCode);
        Assert.Empty(open.AvailableActions);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(employee, ticket, open, TicketWorkflow.StartProcessing)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(admin, ticket, open, TicketWorkflow.Close)).StatusCode);
        await test.AssignAsync(ticket, "staff-a");
        var assigned = await Read(staff, ticket);
        Assert.Equal(TicketWorkflow.StartProcessing, Assert.Single(assigned.AvailableActions).Code);
        var progress = await Success(await Act(staff, ticket, assigned, TicketWorkflow.StartProcessing));
        Assert.Equal(TicketWorkflow.InProgress, progress.CurrentStepCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(staff, ticket, assigned, TicketWorkflow.StartProcessing)).StatusCode);
        Assert.Empty((await Read(supportAdmin, ticket)).AvailableActions);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(supportAdmin, ticket, progress, TicketWorkflow.Resolve)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(employee, ticket, progress, TicketWorkflow.Resolve)).StatusCode);
        var resolved = await Success(await Act(staff, ticket, progress, TicketWorkflow.Resolve));
        Assert.Equal("Running", resolved.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(staff, ticket, resolved, TicketWorkflow.Close)).StatusCode);
        var reopened = await Success(await Act(employee, ticket, resolved, TicketWorkflow.Reopen, "Still not working"));
        Assert.Equal(TicketWorkflow.InProgress, reopened.CurrentStepCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(staff, ticket, progress, TicketWorkflow.Resolve)).StatusCode);
        resolved = await Success(await Act(staff, ticket, reopened, TicketWorkflow.Resolve));
        var closed = await Success(await Act(employee, ticket, resolved, TicketWorkflow.Close));
        Assert.Equal("Completed", closed.Status);
        Assert.Empty(closed.AvailableActions);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(admin, ticket, closed, TicketWorkflow.Reopen)).StatusCode);
        Assert.Equal(new[] { "START", "START_PROCESSING", "RESOLVE", "REOPEN", "RESOLVE", "CLOSE" }, closed.History.Select(x => x.ActionCode));
        Assert.Equal("employee", closed.History.Last().ActorName);
        await using var verify = test.Context();
        var process = await verify.WorkflowProcessInstances.SingleAsync();
        Assert.NotNull(process.CompletedAtUtc);
        Assert.Equal(4, await verify.WorkflowStepInstances.CountAsync());
        Assert.Equal(4, await verify.WorkflowActionInstances.CountAsync());
        Assert.Equal(2, await verify.WorkflowActionerInstances.CountAsync(x => x.ActionerType == WorkflowActionerType.Requester && x.ActionerUserId == test.Users["employee"]));
        Assert.Equal(2, await verify.WorkflowActionerInstances.CountAsync(x => x.ActionerType == WorkflowActionerType.CurrentAssignee && x.ActionerUserId == null));
        Assert.Single(await verify.WorkflowProcessTemplates.ToListAsync());
    }

    [SqlServerFact]
    public async Task DuplicateActionsAndAssignmentChangesCannotUseAStaleStaffAuthorization()
    {
        await using var test = new WorkflowTestDatabase();
        await test.InitializeAsync();
        await Setup(test);
        var ticket = await test.AddTicketAsync();
        await Start(test, ticket);
        await test.AssignAsync(ticket, "staff-a");
        using var staffA = await test.ClientAsync("staff-a");
        using var staffB = await test.ClientAsync("staff-b");
        var open = await Read(staffA, ticket);
        var attempts = await Task.WhenAll(Act(staffA, ticket, open, TicketWorkflow.StartProcessing), Act(staffA, ticket, open, TicketWorkflow.StartProcessing));
        Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.Conflict);
        var progress = await Read(staffA, ticket);
        await using var stale = test.Context();
        _ = await stale.Tickets.SingleAsync(x => x.Id == ticket);
        _ = await stale.WorkflowProcessInstances.SingleAsync(x => x.TicketId == ticket);
        await test.AssignAsync(ticket, "staff-b");
        Assert.Equal(WorkflowResultStatus.NotFound, (await Engine(stale, test, "staff-a")
            .ExecuteAsync(ticket, new(TicketWorkflow.Resolve, progress.RowVersion))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await staffA.GetAsync($"/tickets/{ticket}/workflow")).StatusCode);
        var current = await Read(staffB, ticket);
        Assert.Equal(TicketWorkflow.InProgress, current.CurrentStepCode);
        var resolved = await Success(await Act(staffB, ticket, current, TicketWorkflow.Resolve));
        Assert.Equal("staff-b", resolved.History.Last().ActorName);
        await test.AssignAsync(ticket, "staff-a");
        Assert.Equal(HttpStatusCode.NotFound, (await staffB.GetAsync($"/tickets/{ticket}/workflow")).StatusCode);
        using var employee = await test.ClientAsync("employee");
        var reopened = await Success(await Act(employee, ticket, resolved, TicketWorkflow.Reopen));
        Assert.Equal(TicketWorkflow.Resolve, Assert.Single((await Read(staffA, ticket)).AvailableActions).Code);
        using var admin = await test.ClientAsync("admin");
        (await Act(admin, ticket, reopened, TicketWorkflow.Resolve)).EnsureSuccessStatusCode();
        await using var verify = test.Context();
        Assert.Equal(5, await verify.WorkflowHistory.CountAsync());
    }

    [SqlServerFact]
    public async Task CopiedVersionsSurviveTemplateChangesAndNewInstancesHonorCommentsAndRejectInvalidGraphs()
    {
        await using var test = new WorkflowTestDatabase();
        await test.InitializeAsync();
        await Setup(test);
        await using (var legacy = test.Context())
        {
            foreach (var item in await legacy.WorkflowActionTemplates.ToListAsync())
                item.RequiresComment = false;
            await legacy.SaveChangesAsync();
        }
        var first = await test.AddTicketAsync();
        await Start(test, first);
        await PublishV2(test);
        using var admin = await test.ClientAsync("admin");
        var preserved = await admin.PostAsJsonAsync("/workflow-templates/default", new
        {
        });
        preserved.EnsureSuccessStatusCode();
        Assert.Equal(2, (await preserved.Content.ReadFromJsonAsync<WorkflowTemplateSnapshot>())!.Version);
        var second = await test.AddTicketAsync();
        await Start(test, second);
        await test.AssignAsync(first, "staff-a");
        await test.AssignAsync(second, "staff-a");
        using var staff = await test.ClientAsync("staff-a");
        var v1 = await Read(staff, first);
        var v2 = await Read(staff, second);
        Assert.Equal(1, v1.TemplateVersion);
        Assert.Equal(2, v2.TemplateVersion);
        v1 = await Success(await Act(staff, first, v1, TicketWorkflow.StartProcessing));
        Assert.True(Assert.Single(v1.AvailableActions).RequiresComment);
        await using (var copied = test.Context())
            Assert.False(await copied.WorkflowActionInstances.Where(x => x.ProcessInstanceId == v1.Id && x.Code == TicketWorkflow.Resolve).Select(x => x.RequiresComment).SingleAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Act(staff, first, v1, TicketWorkflow.Resolve, " ")).StatusCode);
        v1 = await Success(await Act(staff, first, v1, TicketWorkflow.Resolve));
        using (var legacyEmployee = await test.ClientAsync("employee"))
        {
            Assert.Empty(v1.AvailableActions);
            var legacyState = await Read(legacyEmployee, first);
            Assert.True(legacyState.AvailableActions.Single(x => x.Code == TicketWorkflow.Reopen).RequiresComment);
            Assert.Equal(HttpStatusCode.BadRequest, (await legacyEmployee.PostAsJsonAsync($"/tickets/{first}/workflow/actions",
                new ExecuteWorkflowAction(TicketWorkflow.Reopen, legacyState.RowVersion))).StatusCode);
        }
        v2 = await Success(await Act(staff, second, v2, TicketWorkflow.StartProcessing));
        Assert.True(Assert.Single(v2.AvailableActions).RequiresComment);
        Assert.Equal(HttpStatusCode.BadRequest, (await Act(staff, second, v2, TicketWorkflow.Resolve, "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Act(staff, second, v2, TicketWorkflow.Resolve, new string('a', 4001))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/tickets/{second}/workflow/actions", new
        {
            actionCode = "RESOLVE",
            rowVersion = "bad base64"
        })).StatusCode);
        v2 = await Success(await Act(staff, second, v2, TicketWorkflow.Resolve, "  Fixed the network  "));
        Assert.Equal("Fixed the network", v2.History.Last().Comment);
        using var employee = await test.ClientAsync("employee");
        v2 = await Success(await Act(employee, second, v2, TicketWorkflow.Reopen));
        v2 = await Success(await Act(staff, second, v2, TicketWorkflow.Resolve, "Verified"));
        (await Act(employee, second, v2, TicketWorkflow.Close)).EnsureSuccessStatusCode();

        await using (var db = test.Context())
        {
            var original = await db.WorkflowProcessTemplates.SingleAsync(x => x.Version == 1);
            Assert.Equal("IT Helpdesk Workflow", (await db.WorkflowProcessInstances.SingleAsync(x => x.TicketId == first)).TemplateName);
            Assert.Equal("Changed template name", original.Name);
            var active = await db.WorkflowProcessTemplates.SingleAsync(x => x.Version == 2);
            var action = await db.WorkflowActionTemplates.SingleAsync(x => x.ProcessTemplateId == active.Id && x.Code == TicketWorkflow.StartProcessing);
            action.ToStepTemplateId = await db.WorkflowStepTemplates.Where(x => x.ProcessTemplateId == active.Id && x.Code == TicketWorkflow.Closed).Select(x => x.Id).SingleAsync();
            await db.SaveChangesAsync();
        }
        var invalidTicket = await test.AddTicketAsync();
        await using (var db = test.Context())
        {
            Assert.Equal(WorkflowResultStatus.Invalid, (await Engine(db, test, "employee").StartAsync(invalidTicket)).Status);
            Assert.False(await db.WorkflowProcessInstances.AnyAsync(x => x.TicketId == invalidTicket));
            var active = await db.WorkflowProcessTemplates.SingleAsync(x => x.IsActive);
            active.IsActive = false;
            await db.SaveChangesAsync();
            Assert.Equal(WorkflowResultStatus.Invalid, (await Engine(db, test, "employee").StartAsync(invalidTicket)).Status);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/workflow-templates/default", new
        {
        })).StatusCode);
    }

    [SqlServerFact]
    public async Task FailedWritesRollbackAllCopiesAndHistoryAndNeverCommitTheCallerTransaction()
    {
        await using var test = new WorkflowTestDatabase();
        await test.InitializeAsync();
        await Setup(test);
        var ticket = await test.AddTicketAsync();
        var failure = new FailHistoryWrite();
        await using (var db = test.Context(failure))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db, test, "employee").StartAsync(ticket));
            Assert.False(await db.WorkflowProcessInstances.AnyAsync());
            Assert.False(await db.WorkflowStepInstances.AnyAsync());
            Assert.False(await db.WorkflowActionInstances.AnyAsync());
            Assert.False(await db.WorkflowActionerInstances.AnyAsync());
            Assert.Equal(WorkflowResultStatus.Success, (await Engine(db, test, "employee").StartAsync(ticket)).Status);
        }
        await test.AssignAsync(ticket, "staff-a");
        await using (var db = test.Context(failure))
        {
            var engine = Engine(db, test, "staff-a");
            var state = (await engine.GetAsync(ticket)).Value!;
            failure.Enabled = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ExecuteAsync(ticket, new(TicketWorkflow.StartProcessing, state.RowVersion)));
            var after = (await engine.GetAsync(ticket)).Value!;
            Assert.Equal(TicketWorkflow.Open, after.CurrentStepCode);
            Assert.Equal(state.RowVersion, after.RowVersion);
            Assert.Single(after.History);
        }
        long nestedTicket;
        await using (var db = test.Context(failure))
        {
            await using var outer = await db.Database.BeginTransactionAsync();
            var item = new Ticket { TicketNumber = "NESTED-ROLLBACK", Title = "Nested test", Description = "Rollback", Category = TicketCategory.Other, RequestedByUserId = test.Users["employee"] };
            db.Tickets.Add(item);
            await db.SaveChangesAsync();
            nestedTicket = item.Id;
            failure.Enabled = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db, test, "employee").StartAsync(item.Id));
            Assert.Same(outer, db.Database.CurrentTransaction);
            Assert.True(await db.Tickets.AnyAsync(x => x.Id == item.Id));
            Assert.False(await db.WorkflowProcessInstances.AnyAsync(x => x.TicketId == item.Id));
            Assert.Equal(WorkflowResultStatus.Success, (await Engine(db, test, "employee").StartAsync(item.Id)).Status);
            Assert.Same(outer, db.Database.CurrentTransaction);
            await outer.RollbackAsync();
        }
        await using var verify = test.Context();
        Assert.False(await verify.Tickets.AnyAsync(x => x.Id == nestedTicket));
        Assert.False(await verify.WorkflowProcessInstances.AnyAsync(x => x.TicketId == nestedTicket));
    }

    private static WorkflowEngine Engine(HelpDeskDbContext db, WorkflowTestDatabase test, string name) => new(db, new TestCurrentUser(test.Users[name]));
    private static async Task Setup(WorkflowTestDatabase test)
    {
        using var admin = await test.ClientAsync("admin");
        (await admin.PostAsJsonAsync("/workflow-templates/default", new
        {
        })).EnsureSuccessStatusCode();
    }
    private static async Task Start(WorkflowTestDatabase test, long ticket)
    {
        await using var db = test.Context();
        Assert.Equal(WorkflowResultStatus.Success, (await Engine(db, test, "employee").StartAsync(ticket)).Status);
    }
    private static async Task<WorkflowSnapshot> Read(HttpClient client, long id) => (await client.GetFromJsonAsync<WorkflowSnapshot>($"/tickets/{id}/workflow"))!;
    private static Task<HttpResponseMessage> Act(HttpClient client, long id, WorkflowSnapshot state, string action, string? comment = null) =>
        client.PostAsJsonAsync($"/tickets/{id}/workflow/actions", new ExecuteWorkflowAction(action, state.RowVersion,
            comment ?? (action is TicketWorkflow.Resolve or TicketWorkflow.Reopen ? "Integration test comment" : null)));
    private static async Task<WorkflowSnapshot> Success(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkflowSnapshot>())!;
    }

    private static async Task PublishV2(WorkflowTestDatabase test)
    {
        await using var db = test.Context();
        await using var tx = await db.Database.BeginTransactionAsync();
        var v1 = await db.WorkflowProcessTemplates.SingleAsync();
        v1.IsActive = false;
        v1.Name = "Changed template name";
        var sourceSteps = await db.WorkflowStepTemplates.ToListAsync();
        var sourceActions = await db.WorkflowActionTemplates.ToListAsync();
        foreach (var action in sourceActions)
            action.RequiresComment = true;
        await db.SaveChangesAsync();
        var v2 = new WorkflowProcessTemplate
        {
            Code = v1.Code,
            Name = "Ticket V2",
            EntityType = v1.EntityType,
            Version = 2,
            IsActive = true,
            IsPublished = true,
            CreatedByUserId = test.Users["admin"],
            PublishedAtUtc = DateTimeOffset.UtcNow
        };
        db.WorkflowProcessTemplates.Add(v2);
        await db.SaveChangesAsync();
        var steps = sourceSteps.ToDictionary(x => x.Id, x => new WorkflowStepTemplate
        {
            ProcessTemplateId = v2.Id,
            Code = x.Code,
            Name = x.Name,
            DisplayOrder = x.DisplayOrder,
            IsInitial = x.IsInitial,
            IsTerminal = x.IsTerminal
        });
        db.WorkflowStepTemplates.AddRange(steps.Values);
        await db.SaveChangesAsync();
        var actions = sourceActions.Select(x => new WorkflowActionTemplate
        {
            ProcessTemplateId = v2.Id,
            Code = x.Code,
            Name = x.Name,
            FromStepTemplateId = steps[x.FromStepTemplateId].Id,
            ToStepTemplateId = steps[x.ToStepTemplateId].Id,
            RequiresComment = x.Code == TicketWorkflow.Resolve
        }).ToArray();
        db.WorkflowActionTemplates.AddRange(actions);
        await db.SaveChangesAsync();
        db.WorkflowActionerTemplates.AddRange(actions.Select(x => new WorkflowActionerTemplate
        {
            ActionTemplateId = x.Id,
            ActionerType = x.Code == TicketWorkflow.Close ? WorkflowActionerType.User : WorkflowActionerType.Role,
            ActionerUserId = x.Code == TicketWorkflow.Close ? test.Users["employee"] : null,
            ActionerRole = x.Code == TicketWorkflow.Close ? null : x.Code == TicketWorkflow.Reopen ? ApplicationRoles.Employee : ApplicationRoles.SupportITStaff
        }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private sealed class FailHistoryWrite : SaveChangesInterceptor
    {
        public bool Enabled { get; set; } = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<WorkflowHistory>().Any(x => x.State == EntityState.Added))
            {
                Enabled = false;
                throw new InvalidOperationException("Injected history failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
