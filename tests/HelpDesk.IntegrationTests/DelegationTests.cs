using System.Net;
using System.Net.Http.Json;
using HelpDesk.Application.Delegations;
using HelpDesk.Application.Tickets;
using HelpDesk.Application.Users;
using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Delegations;
using HelpDesk.Infrastructure.Tickets;
using HelpDesk.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests;

[Collection("SQL Server")]
public sealed class DelegationTests
{
    [SqlServerFact]
    public async Task DateOnlySchedulesUseMalaysiaDaysAndValidateAgainAfterMidnight()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        clock.Now = new(2026, 12, 31, 15, 59, 0, TimeSpan.Zero);
        await test.InitializeAsync();
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        var ticket = await Assigned(employee, admin, test.Users["staff-a"]);
        var today = Today(test);
        var sameDay = await Create(admin, test, today, today);
        Assert.Equal(clock.Now, sameDay.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero), sameDay.EndsAtUtc);
        Assert.Equal("Active", sameDay.Status);

        clock.Now = sameDay.EndsAtUtc.AddTicks(-1);
        await Process(test);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        clock.Now = sameDay.EndsAtUtc;
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(new DateOnly(2027, 1, 1), Today(test));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/delegations", Request(test, today, today))).StatusCode);
        foreach (var dates in new[] {
            new { startDate = "2027-02-30", endDate = "2027-03-01" },
            new { startDate = "2027-01-02T00:00:00+08:00", endDate = "2027-01-02" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/delegations", new
            {
                sourceUserId = test.Users["staff-a"],
                targetUserId = test.Users["staff-b"],
                dates.startDate,
                dates.endDate
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/delegations", new
        {
            sourceUserId = test.Users["staff-a"],
            targetUserId = test.Users["staff-b"]
        })).StatusCode);

        var future = await Create(admin, test, new(2027, 1, 2), new(2027, 1, 2));
        Assert.Equal("Scheduled", future.Status);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 16, 0, 0, TimeSpan.Zero), future.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2027, 1, 2, 16, 0, 0, TimeSpan.Zero), future.EndsAtUtc);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/delegations", Request(test, Today(test), new(2027, 1, 2)))).StatusCode);
        _ = await Create(admin, test, new(2027, 1, 3), new(2027, 1, 3));
        clock.Now = future.StartsAtUtc.AddTicks(-1);
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        clock.Now = future.StartsAtUtc;
        await Process(test);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        clock.Now = future.EndsAtUtc;
        await Process(test);
        var detail = (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{future.Id}"))!;
        Assert.Equal("Completed", detail.Delegation.Status);
        Assert.Equal(1, detail.Delegation.ReturnedTickets);

        var leapDay = await Create(admin, test, new(2028, 2, 29), new(2028, 2, 29));
        Assert.Equal(new DateTimeOffset(2028, 2, 29, 16, 0, 0, TimeSpan.Zero), leapDay.EndsAtUtc);
    }

    [SqlServerFact]
    public async Task ActivationRoutingAndReturnPreserveStatesManualAssignmentsAndPermissions()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await AddC(test);
        using var admin = await test.ClientAsync("support-admin");
        using var employee = await test.ClientAsync("employee");
        using var a = await test.ClientAsync("staff-a");
        using var b = await test.ClientAsync("staff-b");
        await Setup(test);
        var open = await Assigned(employee, admin, test.Users["staff-a"]);
        var resolvedDuring = await Assigned(employee, admin, test.Users["staff-a"]);
        await Act(a, resolvedDuring.Ticket.Id, TicketWorkflow.StartProcessing);
        var closedDuring = await Assigned(employee, admin, test.Users["staff-a"]);
        var toC = await Assigned(employee, admin, test.Users["staff-a"]);
        var backToB = await Assigned(employee, admin, test.Users["staff-a"]);
        var resolvedBefore = await Assigned(employee, admin, test.Users["staff-a"]);
        await Act(a, resolvedBefore.Ticket.Id, TicketWorkflow.StartProcessing);
        await Act(a, resolvedBefore.Ticket.Id, TicketWorkflow.Resolve);
        var closedBefore = await Assigned(employee, admin, test.Users["staff-a"]);
        await Act(a, closedBefore.Ticket.Id, TicketWorkflow.StartProcessing);
        await Act(a, closedBefore.Ticket.Id, TicketWorkflow.Resolve);
        await Act(employee, closedBefore.Ticket.Id, TicketWorkflow.Close);
        var bOwn = await Assigned(employee, admin, test.Users["staff-b"]);
        var delegation = await Create(admin, test, null, Today(test));
        Assert.Equal("Active", delegation.Status);
        Assert.Equal(5, delegation.TotalTickets);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/tickets/{open.Ticket.Id}")).StatusCode);
        Assert.Equal("OPEN", (await Read(b, open.Ticket.Id)).Ticket.Status);
        Assert.Equal("IN_PROGRESS", (await Read(b, resolvedDuring.Ticket.Id)).Ticket.Status);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, resolvedBefore.Ticket.Id)).Ticket.AssignedToUserId);
        var routed = await Assigned(employee, admin, test.Users["staff-a"]);
        Assert.Equal(test.Users["staff-b"], routed.Ticket.AssignedToUserId);
        await Act(b, resolvedDuring.Ticket.Id, TicketWorkflow.Resolve);
        await Act(b, closedDuring.Ticket.Id, TicketWorkflow.StartProcessing);
        await Act(b, closedDuring.Ticket.Id, TicketWorkflow.Resolve);
        await Act(employee, closedDuring.Ticket.Id, TicketWorkflow.Close);
        _ = await Assign(admin, await Read(admin, toC.Ticket.Id), test.Users["staff-c"]);
        _ = await Assign(admin, await Read(admin, backToB.Ticket.Id), test.Users["staff-c"]);
        _ = await Assign(admin, await Read(admin, backToB.Ticket.Id), test.Users["staff-b"]);
        clock.Now = delegation.EndsAtUtc;
        await Process(test);
        await Process(test);
        var done = (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{delegation.Id}"))!;
        Assert.Equal("Completed", done.Delegation.Status);
        Assert.Equal(3, done.Delegation.ReturnedTickets);
        Assert.Equal(1, done.Delegation.SkippedClosedTickets);
        Assert.Equal(2, done.Delegation.SkippedReassignedTickets);
        Assert.Equal(6, done.Tickets.Count);
        Assert.Contains(done.Tickets, x => x.TicketId == routed.Ticket.Id && x.EntryReason == "NewAssignment");
        Assert.Equal(test.Users["staff-a"], (await Read(admin, open.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-c"], (await Read(admin, toC.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, backToB.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, bOwn.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/tickets/{open.Ticket.Id}")).StatusCode);
        Assert.Equal("RESOLVED", (await Read(a, resolvedDuring.Ticket.Id)).Ticket.Status);
        await Act(employee, resolvedDuring.Ticket.Id, TicketWorkflow.Reopen);
        Assert.Equal("IN_PROGRESS", (await Read(a, resolvedDuring.Ticket.Id)).Ticket.Status);
        await using var db = test.Context();
        Assert.Equal(6, await db.DelegationTickets.CountAsync());
        Assert.Equal(3, await db.TicketHistories.CountAsync(x => x.EventType == TicketHistoryEventType.DelegationReturned));
    }

    [SqlServerFact]
    public async Task AuthorizationValidationOverlapAdjacentPeriodsAndDelayedStartupAreHandled()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await AddC(test);
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        using var staff = await test.ClientAsync("staff-a");
        using var anonymous = test.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/delegations")).StatusCode);
        foreach (var denied in new[] { employee, staff })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/delegations")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/delegations/1")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync("/delegations", Request(test, null, Today(test)))).StatusCode);
        }
        var valid = Request(test, Today(test).AddDays(1), Today(test).AddDays(1));
        foreach (var invalid in new[] { valid with { TargetUserId = valid.SourceUserId }, valid with { TargetUserId = test.Users["employee"] },
            valid with { EndDate = Today(test) }, valid with { StartDate = Today(test).AddDays(-1) },
            valid with { StartDate = default }, valid with { EndDate = default }, valid with { EndDate = DateOnly.MaxValue },
            valid with { Reason = new string('x',1001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/delegations", invalid)).StatusCode);
        var ticket = await Assigned(employee, admin, test.Users["staff-a"]);
        var scheduled = await Create(admin, test, valid.StartDate, valid.EndDate);
        Assert.Equal("Scheduled", scheduled.Status);
        foreach (var (from, to) in new[] { ("staff-a", "staff-c"), ("staff-b", "staff-c"), ("staff-c", "staff-a"), ("staff-c", "staff-b") })
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/delegations", valid with
            {
                SourceUserId = test.Users[from],
                TargetUserId = test.Users[to]
            })).StatusCode);
        var adjacentResponse = await admin.PostAsJsonAsync("/delegations", valid with
        {
            SourceUserId = test.Users["staff-b"],
            TargetUserId = test.Users["staff-c"],
            StartDate = valid.EndDate.AddDays(1),
            EndDate = valid.EndDate.AddDays(1)
        });
        Assert.Equal(HttpStatusCode.Created, adjacentResponse.StatusCode);
        var adjacent = (await adjacentResponse.Content.ReadFromJsonAsync<DelegationItem>())!;
        clock.Now = scheduled.StartsAtUtc;
        var routed = await Assigned(employee, admin, test.Users["staff-a"]);
        Assert.Equal(test.Users["staff-b"], routed.Ticket.AssignedToUserId);
        await Process(test);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        clock.Now = adjacent.EndsAtUtc;
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, routed.Ticket.Id)).Ticket.AssignedToUserId);
        var history = (await admin.GetFromJsonAsync<DelegationPage>("/delegations"))!;
        Assert.All(history.Items, x => Assert.Equal("Completed", x.Status));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/delegations/999999")).StatusCode);
    }

    [SqlServerFact]
    public async Task UnavailableAccountsPauseWorkAndResumeWithoutOverwritingManualReassignment()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await AddC(test);
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        var first = await Assigned(employee, admin, test.Users["staff-a"]);
        var manual = await Assigned(employee, admin, test.Users["staff-a"]);
        var d = await Create(admin, test, Today(test).AddDays(1), Today(test).AddDays(1));
        await Account(test, "staff-b", active: false);
        clock.Now = d.StartsAtUtc;
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, first.Ticket.Id)).Ticket.AssignedToUserId);
        var detail = (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{d.Id}"))!;
        Assert.Equal("NeedsAttention", detail.Delegation.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/tickets/{first.Ticket.Id}/assignment", new AssignTicketRequest(test.Users["staff-a"], (await Read(admin, first.Ticket.Id)).RowVersion))).StatusCode);
        await Account(test, "staff-b", active: true);
        await Process(test);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, first.Ticket.Id)).Ticket.AssignedToUserId);
        await Account(test, "staff-a", active: false);
        clock.Now = d.EndsAtUtc;
        await Process(test);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, first.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal("NeedsAttention", (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{d.Id}"))!.Delegation.Status);
        _ = await Assign(admin, await Read(admin, manual.Ticket.Id), test.Users["staff-c"]);
        await Process(test);
        await Account(test, "staff-a", active: true, role: ApplicationRoles.Employee);
        await Process(test);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, first.Ticket.Id)).Ticket.AssignedToUserId);
        await Account(test, "staff-a", active: true, role: ApplicationRoles.SupportITStaff);
        await Process(test);
        detail = (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{d.Id}"))!;
        Assert.Equal("Completed", detail.Delegation.Status);
        Assert.Equal(1, detail.Delegation.ReturnedTickets);
        Assert.Equal(1, detail.Delegation.SkippedReassignedTickets);
        Assert.Equal(test.Users["staff-c"], (await Read(admin, manual.Ticket.Id)).Ticket.AssignedToUserId);
    }

    [SqlServerFact]
    public async Task FailedCreationAssignmentAndReturnRollbackAllRelatedWrites()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        var ticket = await Assigned(employee, admin, test.Users["staff-a"]);
        await using (var db = test.Context(new FailDelegationHistory()))
        {
            var service = new DelegationService(db, new TestCurrentUser(test.Users["admin"]), clock, test.Processor(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Request(test, null, Today(test))));
        }
        await using (var db = test.Context())
        {
            Assert.Empty(await db.Delegations.ToListAsync());
            Assert.Empty(await db.DelegationTickets.ToListAsync());
        }
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        var d = await Create(admin, test, null, Today(test));
        var unassigned = await New(employee);
        await using (var db = test.Context(new FailDelegationHistory()))
        {
            var current = new TestCurrentUser(test.Users["admin"]);
            var service = new TicketService(db, current, new WorkflowEngine(db, current), test.Files(db), test.Processor(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignAsync(unassigned.Ticket.Id, new(test.Users["staff-a"], unassigned.RowVersion)));
        }
        Assert.Null((await Read(admin, unassigned.Ticket.Id)).Ticket.AssignedToUserId);
        clock.Now = d.EndsAtUtc;
        await using (var db = test.Context(new FailDelegationHistory()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => test.Processor(db).ProcessAsync(d.Id));
        await using (var db = test.Context())
        {
            Assert.Null((await db.Delegations.SingleAsync()).CompletedAtUtc);
            Assert.Equal(DelegationReturnOutcome.AwaitingEnd, (await db.DelegationTickets.SingleAsync()).ReturnOutcome);
        }
        Assert.Equal(test.Users["staff-b"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
    }

    [SqlServerFact]
    public async Task ConcurrentSchedulesWorkersAndManualAssignmentsDoNotLoseUpdates()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await AddC(test);
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var support = await test.ClientAsync("support-admin");
        using var employee = await test.ClientAsync("employee");
        var ticket = await Assigned(employee, admin, test.Users["staff-a"]);
        var body = Request(test, Today(test).AddDays(1), Today(test).AddDays(1));
        var creates = await Task.WhenAll(admin.PostAsJsonAsync("/delegations", body), support.PostAsJsonAsync("/delegations", body));
        Assert.Single(creates, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(creates, x => x.StatusCode == HttpStatusCode.Conflict);
        var d = (await creates.Single(x => x.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<DelegationItem>())!;
        clock.Now = d.StartsAtUtc;
        await Task.WhenAll(Process(test), Process(test));
        var delegated = await Read(admin, ticket.Ticket.Id);
        Assert.Equal(test.Users["staff-b"], delegated.Ticket.AssignedToUserId);
        clock.Now = d.EndsAtUtc;
        var manual = support.PutAsJsonAsync($"/tickets/{ticket.Ticket.Id}/assignment", new AssignTicketRequest(test.Users["staff-c"], delegated.RowVersion));
        await Task.WhenAll(manual, Process(test));
        if ((await manual).StatusCode == HttpStatusCode.Conflict)
            _ = await Assign(support, await Read(support, ticket.Ticket.Id), test.Users["staff-c"]);
        else
            Assert.Equal(HttpStatusCode.OK, (await manual).StatusCode);
        Assert.Equal(test.Users["staff-c"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        await Process(test);
        await using var verify = test.Context();
        Assert.Single(await verify.DelegationTickets.ToListAsync());
        Assert.Single(await verify.TicketHistories.Where(x => x.EventType == TicketHistoryEventType.Delegated).ToListAsync());
        Assert.NotNull((await verify.Delegations.SingleAsync()).CompletedAtUtc);
    }

    [SqlServerFact]
    public async Task ExplicitAssignmentToSourceRenewsOneLedgerEntryEvenWhenAlreadyOwnedByDelegate()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await AddC(test);
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        var ticket = await Assigned(employee, admin, test.Users["staff-b"]);
        var d = await Create(admin, test, null, Today(test));
        ticket = await Assign(admin, ticket, test.Users["staff-a"]);
        Assert.Equal(test.Users["staff-b"], ticket.Ticket.AssignedToUserId);
        var version = ticket.RowVersion;
        ticket = await Assign(admin, ticket, test.Users["staff-a"]);
        Assert.Equal(version, ticket.RowVersion);
        ticket = await Assign(admin, ticket, test.Users["staff-c"]);
        ticket = await Assign(admin, ticket, test.Users["staff-a"]);
        clock.Now = d.EndsAtUtc;
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        await using var db = test.Context();
        Assert.Single(await db.DelegationTickets.ToListAsync());
        Assert.Equal(DelegationReturnOutcome.Returned, (await db.DelegationTickets.SingleAsync()).ReturnOutcome);
    }

    [SqlServerFact]
    public async Task UnfinishedReturnBlocksTheNextPeriodUntilTheOriginalAccountIsRestored()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await AddC(test);
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        var aTicket = await Assigned(employee, admin, test.Users["staff-a"]);
        var bOwn = await Assigned(employee, admin, test.Users["staff-b"]);
        var first = await Create(admin, test, null, Today(test));
        var response = await admin.PostAsJsonAsync("/delegations", new CreateDelegation(test.Users["staff-b"], test.Users["staff-c"], Day(first.EndsAtUtc), Day(first.EndsAtUtc), null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var next = (await response.Content.ReadFromJsonAsync<DelegationItem>())!;
        await Account(test, "staff-a", active: false);
        clock.Now = first.EndsAtUtc.AddMinutes(1);
        await Process(test);
        Assert.Equal("NeedsAttention", (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{next.Id}"))!.Delegation.Status);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, aTicket.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, bOwn.Ticket.Id)).Ticket.AssignedToUserId);
        await Account(test, "staff-a", active: true);
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, aTicket.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-c"], (await Read(admin, bOwn.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal("Active", (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{next.Id}"))!.Delegation.Status);
        clock.Now = next.EndsAtUtc.AddMinutes(1);
        await Process(test);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, bOwn.Ticket.Id)).Ticket.AssignedToUserId);
    }

    [SqlServerFact]
    public async Task ScheduledCancellationIsAuthorizedAuditedIdempotentAndNeverHandsOverTickets()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var support = await test.ClientAsync("support-admin");
        using var employee = await test.ClientAsync("employee");
        using var staff = await test.ClientAsync("staff-a");
        using var anonymous = test.Factory.CreateClient();
        var ticket = await Assigned(employee, admin, test.Users["staff-a"]);
        var scheduled = await Create(admin, test, Today(test).AddDays(1), Today(test).AddDays(1));
        Assert.True(scheduled.CanCancel);
        Assert.False(scheduled.CanEndEarly);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Stop(anonymous, scheduled, "cancel")).StatusCode);
        foreach (var denied in new[] { employee, staff })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Stop(denied, scheduled, "cancel")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Stop(denied, scheduled, "end-early")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Stop(admin, scheduled, "end-early")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/delegations/{scheduled.Id}/cancel", new StopDelegation([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/delegations/{scheduled.Id}/cancel", new StopDelegation(new byte[8]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/delegations/999999/cancel", new StopDelegation(scheduled.RowVersion))).StatusCode);
        var response = await Stop(support, scheduled, "cancel");
        response.EnsureSuccessStatusCode();
        var cancelled = (await response.Content.ReadFromJsonAsync<DelegationItem>())!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("support-admin", cancelled.StoppedByName);
        Assert.Equal(clock.Now, cancelled.StoppedAtUtc);
        Assert.Equal(scheduled.EndsAtUtc, cancelled.EndsAtUtc);
        Assert.False(cancelled.CanCancel);
        Assert.False(cancelled.CanEndEarly);
        var repeated = await Stop(admin, scheduled, "cancel");
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(cancelled.RowVersion, (await repeated.Content.ReadFromJsonAsync<DelegationItem>())!.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await Stop(admin, cancelled, "end-early")).StatusCode);
        _ = await Create(admin, test, Day(scheduled.StartsAtUtc), Day(scheduled.EndsAtUtc.AddTicks(-1)));
        clock.Now = scheduled.EndsAtUtc.AddHours(1);
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        await using var db = test.Context();
        Assert.Empty(await db.DelegationTickets.Where(x => x.DelegationId == scheduled.Id).ToListAsync());
        Assert.Empty(await db.TicketHistories.Where(x => x.DelegationId == scheduled.Id).ToListAsync());
        Assert.Equal(test.Users["support-admin"], (await db.Delegations.SingleAsync(x => x.Id == scheduled.Id)).StoppedByUserId);
    }

    [SqlServerFact]
    public async Task EndingEarlyReturnsEligibleTicketsPreservesManualAndClosedAssignmentsAndStopsRouting()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await AddC(test);
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        using var a = await test.ClientAsync("staff-a");
        using var b = await test.ClientAsync("staff-b");
        var open = await Assigned(employee, admin, test.Users["staff-a"]);
        var resolved = await Assigned(employee, admin, test.Users["staff-a"]);
        var closed = await Assigned(employee, admin, test.Users["staff-a"]);
        var manual = await Assigned(employee, admin, test.Users["staff-a"]);
        var backToB = await Assigned(employee, admin, test.Users["staff-a"]);
        var own = await Assigned(employee, admin, test.Users["staff-b"]);
        var active = await Create(admin, test, null, Today(test).AddDays(1));
        Assert.True(active.CanEndEarly);
        Assert.False(active.CanCancel);
        await Act(b, resolved.Ticket.Id, TicketWorkflow.StartProcessing);
        await Act(b, resolved.Ticket.Id, TicketWorkflow.Resolve);
        await Act(b, closed.Ticket.Id, TicketWorkflow.StartProcessing);
        await Act(b, closed.Ticket.Id, TicketWorkflow.Resolve);
        await Act(employee, closed.Ticket.Id, TicketWorkflow.Close);
        _ = await Assign(admin, await Read(admin, manual.Ticket.Id), test.Users["staff-c"]);
        var changed = await Assign(admin, await Read(admin, backToB.Ticket.Id), test.Users["staff-c"]);
        _ = await Assign(admin, changed, test.Users["staff-b"]);
        clock.Now = clock.Now.AddMinutes(1);
        var response = await Stop(admin, active, "end-early");
        response.EnsureSuccessStatusCode();
        var ended = (await response.Content.ReadFromJsonAsync<DelegationItem>())!;
        Assert.Equal("EndedEarly", ended.Status);
        Assert.Equal(active.EndsAtUtc, ended.EndsAtUtc);
        Assert.Equal(2, ended.ReturnedTickets);
        Assert.Equal(1, ended.SkippedClosedTickets);
        Assert.Equal(2, ended.SkippedReassignedTickets);
        Assert.Equal("OPEN", (await Read(a, open.Ticket.Id)).Ticket.Status);
        Assert.Equal("RESOLVED", (await Read(a, resolved.Ticket.Id)).Ticket.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/tickets/{open.Ticket.Id}")).StatusCode);
        Assert.Equal(test.Users["staff-c"], (await Read(admin, manual.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, backToB.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, own.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal("CLOSED", (await Read(admin, closed.Ticket.Id)).Ticket.Status);
        var later = await Assigned(employee, admin, test.Users["staff-a"]);
        Assert.Equal(test.Users["staff-a"], later.Ticket.AssignedToUserId);
        await Act(employee, resolved.Ticket.Id, TicketWorkflow.Reopen);
        Assert.Equal("IN_PROGRESS", (await Read(a, resolved.Ticket.Id)).Ticket.Status);
        (await Stop(admin, active, "end-early")).EnsureSuccessStatusCode();
        clock.Now = active.EndsAtUtc.AddHours(1);
        await Process(test);
        await using var db = test.Context();
        Assert.Equal(2, await db.TicketHistories.CountAsync(x => x.EventType == TicketHistoryEventType.DelegationReturned));
        Assert.Equal(5, await db.DelegationTickets.CountAsync());
        Assert.All(await db.TicketHistories.Where(x => x.EventType == TicketHistoryEventType.DelegationReturned).ToListAsync(), x => Assert.Contains("ended early", x.Comment));
    }

    [SqlServerFact]
    public async Task EarlyEndWithUnavailableSourceRetriesBeforeTheOriginalEndAndPreservesManualChanges()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await AddC(test);
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        var ticket = await Assigned(employee, admin, test.Users["staff-a"]);
        var manual = await Assigned(employee, admin, test.Users["staff-a"]);
        var active = await Create(admin, test, null, Today(test).AddDays(1));
        await Account(test, "staff-a", active: false);
        var response = await Stop(admin, active, "end-early");
        response.EnsureSuccessStatusCode();
        var stopped = (await response.Content.ReadFromJsonAsync<DelegationItem>())!;
        Assert.Equal("NeedsAttention", stopped.Status);
        Assert.Equal(2, stopped.PendingTickets);
        Assert.NotNull(stopped.StoppedAtUtc);
        Assert.False(stopped.CanEndEarly);
        Assert.False(stopped.CanCancel);
        Assert.Equal(test.Users["staff-b"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        _ = await Assign(admin, await Read(admin, manual.Ticket.Id), test.Users["staff-c"]);
        await Process(test);
        await Account(test, "staff-a", active: true);
        var later = await Assigned(employee, admin, test.Users["staff-a"]);
        Assert.Equal(test.Users["staff-a"], later.Ticket.AssignedToUserId);
        await Process(test);
        var done = (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{active.Id}"))!;
        Assert.Equal("EndedEarly", done.Delegation.Status);
        Assert.Equal(1, done.Delegation.ReturnedTickets);
        Assert.Equal(1, done.Delegation.SkippedReassignedTickets);
        Assert.Equal(stopped.StoppedAtUtc, done.Delegation.StoppedAtUtc);
        Assert.True(clock.Now < active.EndsAtUtc);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
    }

    [SqlServerFact]
    public async Task BoundariesAndDelayedActivationCannotBeCancelledAsScheduledOrHandOverAfterStopping()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var employee = await test.ClientAsync("employee");
        var original = await Assigned(employee, admin, test.Users["staff-a"]);
        var scheduled = await Create(admin, test, Today(test).AddDays(1), Today(test).AddDays(1));
        clock.Now = scheduled.StartsAtUtc;
        Assert.Equal(HttpStatusCode.Conflict, (await Stop(admin, scheduled, "cancel")).StatusCode);
        var routed = await Assigned(employee, admin, test.Users["staff-a"]);
        Assert.Equal(test.Users["staff-b"], routed.Ticket.AssignedToUserId);
        (await Stop(admin, scheduled, "end-early")).EnsureSuccessStatusCode();
        await Process(test);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, original.Ticket.Id)).Ticket.AssignedToUserId);
        Assert.Equal(test.Users["staff-a"], (await Read(admin, routed.Ticket.Id)).Ticket.AssignedToUserId);
        var natural = await Create(admin, test, null, Today(test));
        clock.Now = natural.EndsAtUtc.AddMinutes(1);
        await Process(test);
        natural = (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{natural.Id}"))!.Delegation;
        Assert.Equal("Completed", natural.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Stop(admin, natural, "end-early")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Stop(admin, natural, "cancel")).StatusCode);
    }

    [SqlServerFact]
    public async Task EarlyEndRollsBackItsAuditOnFailureAndConcurrentRetriesReturnOnlyOnce()
    {
        await using var test = new WorkflowTestDatabase();
        var clock = Clock(test);
        await test.InitializeAsync();
        await Setup(test);
        using var admin = await test.ClientAsync("admin");
        using var support = await test.ClientAsync("support-admin");
        using var employee = await test.ClientAsync("employee");
        var ticket = await Assigned(employee, admin, test.Users["staff-a"]);
        var active = await Create(admin, test, null, Today(test));
        await using (var db = test.Context(new FailDelegationHistory()))
        {
            var service = new DelegationService(db, new TestCurrentUser(test.Users["admin"]), clock, test.Processor(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EndEarlyAsync(active.Id, new(active.RowVersion)));
        }
        await using (var db = test.Context())
        {
            var row = await db.Delegations.SingleAsync();
            Assert.Null(row.StoppedAtUtc);
            Assert.Null(row.StoppedByUserId);
            Assert.Null(row.CompletedAtUtc);
        }
        active = (await admin.GetFromJsonAsync<DelegationDetail>($"/delegations/{active.Id}"))!.Delegation;
        Assert.Equal(test.Users["staff-b"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
        var stops = await Task.WhenAll(Stop(admin, active, "end-early"), Stop(support, active, "end-early"));
        Assert.All(stops, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        await Process(test);
        await using var verify = test.Context();
        Assert.Equal(1, await verify.TicketHistories.CountAsync(x => x.EventType == TicketHistoryEventType.DelegationReturned));
        Assert.Equal(test.Users["staff-a"], (await Read(admin, ticket.Ticket.Id)).Ticket.AssignedToUserId);
    }

    private static Task<HttpResponseMessage> Stop(HttpClient client, DelegationItem item, string action) =>
        client.PostAsJsonAsync($"/delegations/{item.Id}/{action}", new StopDelegation(item.RowVersion));

    private static ManualClock Clock(WorkflowTestDatabase test)
    {
        var clock = new ManualClock();
        test.Factory.Clock = clock;
        return clock;
    }
    private static DateOnly Day(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(TimeSpan.FromHours(8)).DateTime);
    private static DateOnly Today(WorkflowTestDatabase t) => Day(t.Factory.Clock.GetUtcNow());
    private static CreateDelegation Request(WorkflowTestDatabase t, DateOnly? start, DateOnly end) => new(t.Users["staff-a"], t.Users["staff-b"], start ?? Today(t), end, "Annual leave");
    private static async Task<DelegationItem> Create(HttpClient client, WorkflowTestDatabase t, DateOnly? start, DateOnly end)
    {
        var response = await client.PostAsJsonAsync("/delegations", Request(t, start, end));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DelegationItem>())!;
    }
    private static async Task Setup(WorkflowTestDatabase t)
    {
        using var a = await t.ClientAsync("admin");
        (await a.PostAsJsonAsync("/workflow-templates/default", new
        {
        })).EnsureSuccessStatusCode();
    }
    private static async Task<TicketDetail> New(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/tickets", new CreateTicketRequest("Delegation test", "Disposable data", TicketCategory.Other));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<TicketDetail>())!;
    }
    private static async Task<TicketDetail> Assigned(HttpClient employee, HttpClient admin, int user) => await Assign(admin, await New(employee), user);
    private static async Task<TicketDetail> Read(HttpClient c, long id) => (await c.GetFromJsonAsync<TicketDetail>($"/tickets/{id}"))!;
    private static async Task<TicketDetail> Assign(HttpClient c, TicketDetail t, int user)
    {
        var r = await c.PutAsJsonAsync($"/tickets/{t.Ticket.Id}/assignment", new AssignTicketRequest(user, t.RowVersion));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<TicketDetail>())!;
    }
    private static async Task Act(HttpClient c, long id, string action)
    {
        var t = await Read(c, id);
        (await c.PostAsJsonAsync($"/tickets/{id}/workflow/actions", new ExecuteWorkflowAction(action, t.Workflow.RowVersion, "Verified by delegation test"))).EnsureSuccessStatusCode();
    }
    private static async Task Process(WorkflowTestDatabase t)
    {
        await using var db = t.Context();
        await t.Processor(db).ProcessDueAsync();
    }
    private static async Task AddC(WorkflowTestDatabase t)
    {
        await using var scope = t.Factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUserAccountStore>().CreateAsync(new("staff-c@example.invalid", "staff-c", "Integration-only!42", ApplicationRoles.SupportITStaff));
        Assert.True(result.Succeeded);
        t.Users.Add("staff-c", result.UserId!.Value);
    }
    private static async Task Account(WorkflowTestDatabase t, string name, bool active, string? role = null)
    {
        await using var scope = t.Factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IUserAccountStore>();
        Assert.True((await store.SetActiveAsync(t.Users[name], active)).Succeeded);
        if (role is not null)
            Assert.True((await store.SetRoleAsync(t.Users[name], role)).Succeeded);
    }
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 25, 4, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FailDelegationHistory : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<TicketHistory>().Any(x => x.State == EntityState.Added && x.Entity.DelegationId.HasValue))
                throw new InvalidOperationException("Injected delegation failure");
            return base.SavingChangesAsync(data, result, cancellationToken);
        }
    }
}
