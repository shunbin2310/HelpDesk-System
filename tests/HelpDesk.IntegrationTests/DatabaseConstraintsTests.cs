using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Delegations;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.IntegrationTests;

[Collection("SQL Server")]
public sealed class DatabaseConstraintsTests
{
    private static HelpDeskDbContext Context() => new(new DbContextOptionsBuilder<HelpDeskDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("HELPDESK_TEST_CONNECTION")).Options);

    [SqlServerFact]
    public async Task DatabaseRejectsSecondRoleAndDuplicateEmail()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var user = await AddUser(db);
            var roleIds = await db.Roles.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            db.UserRoles.Add(new() { UserId = user.Id, RoleId = roleIds[0] });
            await db.SaveChangesAsync();
            db.UserRoles.Add(new() { UserId = user.Id, RoleId = roleIds[1] });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            var duplicate = NewUser();
            duplicate.Email = user.Email;
            duplicate.NormalizedEmail = user.NormalizedEmail;
            db.Users.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally { await transaction.RollbackAsync(); }
    }

    [SqlServerFact]
    public async Task AttachmentsCannotReferenceAnotherTicketsMessageAndStaleUpdatesAreRejected()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var user = await AddUser(db);
            var first = await AddTicket(db, user.Id);
            var second = await AddTicket(db, user.Id);
            var message = new TicketMessage
            {
                TicketId = first.Id, AuthorUserId = user.Id,
                MessageType = TicketMessageType.InternalNote, Body = "Internal test note"
            };
            db.TicketMessages.Add(message);
            await db.SaveChangesAsync();
            db.Attachments.Add(new()
            {
                TicketId = second.Id, TicketMessageId = message.Id, UploadedByUserId = user.Id,
                OriginalFileName = "test.txt", StorageKey = Guid.NewGuid().ToString("N"), ContentType = "text/plain"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            var ticket = await db.Tickets.SingleAsync(x => x.Id == first.Id);
            Assert.Equal(8, ticket.RowVersion.Length);
            ticket.Title = "Stale update";
            db.Entry(ticket).Property(x => x.RowVersion).OriginalValue = new byte[8];
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        }
        finally { await transaction.RollbackAsync(); }
    }

    [SqlServerFact]
    public async Task WorkflowRelationshipsCannotCrossTemplatesOrTicketInstances()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var user = await AddUser(db);
            var firstTicket = await AddTicket(db, user.Id);
            var secondTicket = await AddTicket(db, user.Id);
            var first = NewTemplate(user.Id);
            var second = NewTemplate(user.Id);
            db.AddRange(first, second);
            await db.SaveChangesAsync();
            var firstOpen = new WorkflowStepTemplate
            {
                ProcessTemplateId = first.Id, Code = TicketWorkflow.Open, Name = "Open", IsInitial = true
            };
            var secondProgress = new WorkflowStepTemplate
            {
                ProcessTemplateId = second.Id, Code = TicketWorkflow.InProgress, Name = "In Progress"
            };
            db.AddRange(firstOpen, secondProgress);
            await db.SaveChangesAsync();
            db.Add(new WorkflowActionTemplate
            {
                ProcessTemplateId = first.Id, FromStepTemplateId = firstOpen.Id,
                ToStepTemplateId = secondProgress.Id, Code = TicketWorkflow.StartProcessing, Name = "Start"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            var firstInstance = NewInstance(firstTicket.Id, first);
            var secondInstance = NewInstance(secondTicket.Id, second);
            db.AddRange(firstInstance, secondInstance);
            await db.SaveChangesAsync();
            var secondStep = new WorkflowStepInstance
            {
                ProcessInstanceId = secondInstance.Id, SourceStepTemplateId = secondProgress.Id,
                Code = TicketWorkflow.InProgress, Name = "In Progress"
            };
            db.Add(secondStep);
            await db.SaveChangesAsync();
            firstInstance.CurrentStepInstanceId = secondStep.Id;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            var ownStep = new WorkflowStepInstance
            {
                ProcessInstanceId = firstInstance.Id, SourceStepTemplateId = firstOpen.Id,
                Code = TicketWorkflow.Open, Name = "Open", IsInitial = true
            };
            db.Add(ownStep);
            await db.SaveChangesAsync();
            var current = await db.WorkflowProcessInstances.SingleAsync(x => x.Id == firstInstance.Id);
            current.CurrentStepInstanceId = ownStep.Id;
            await db.SaveChangesAsync();
            Assert.Equal(ownStep.Id, current.CurrentStepInstanceId);
        }
        finally { await transaction.RollbackAsync(); }
    }

    [SqlServerFact]
    public async Task InvalidDelegationPeriodsAndDuplicateHandoffsAreRejected()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var first = await AddUser(db);
            var second = await AddUser(db);
            var ticket = await AddTicket(db, first.Id);
            var starts = DateTimeOffset.UtcNow;
            db.Delegations.Add(new()
            {
                SourceUserId = first.Id, TargetUserId = second.Id, CreatedByUserId = first.Id,
                StartsAtUtc = starts, EndsAtUtc = starts.AddHours(-1)
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            var delegation = new Delegation
            {
                SourceUserId = first.Id, TargetUserId = second.Id, CreatedByUserId = first.Id,
                StartsAtUtc = starts, EndsAtUtc = starts.AddHours(1)
            };
            db.Add(delegation);
            await db.SaveChangesAsync();
            DelegationTicket Handoff() => new()
            {
                DelegationId = delegation.Id, TicketId = ticket.Id,
                OriginalAssigneeUserId = first.Id, DelegatedToUserId = second.Id
            };
            db.Add(Handoff());
            await db.SaveChangesAsync();
            db.Add(Handoff());
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally { await transaction.RollbackAsync(); }
    }

    private static ApplicationUser NewUser()
    {
        var email = $"stage1-{Guid.NewGuid():N}@example.invalid";
        return new() { Email = email, NormalizedEmail = email.ToUpperInvariant(),
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), FullName = "Stage 1 test" };
    }

    private static async Task<ApplicationUser> AddUser(HelpDeskDbContext db)
    {
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Ticket> AddTicket(HelpDeskDbContext db, int requester)
    {
        var ticket = new Ticket
        {
            TicketNumber = $"TEST-{Guid.NewGuid():N}"[..32],
            Title = "Stage 1 test", Description = "Constraint verification",
            Category = TicketCategory.Network, RequestedByUserId = requester
        };
        db.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private static WorkflowProcessTemplate NewTemplate(int creator) => new()
    {
        Code = $"TEST_{Guid.NewGuid():N}", Name = "Test workflow", EntityType = TicketWorkflow.EntityType,
        CreatedByUserId = creator
    };

    private static WorkflowProcessInstance NewInstance(long ticketId, WorkflowProcessTemplate template) => new()
    {
        TicketId = ticketId, ProcessTemplateId = template.Id,
        TemplateCode = template.Code, TemplateName = template.Name, TemplateVersion = template.Version
    };
}
