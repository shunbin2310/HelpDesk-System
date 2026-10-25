using HelpDesk.Application.Authentication;
using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Workflows;

public sealed class WorkflowTemplateService(HelpDeskDbContext db, ICurrentUserContext current) : IWorkflowTemplateService
{
    public async Task<WorkflowResult<WorkflowTemplateSnapshot>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if ((await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken))?.Role != ApplicationRoles.SystemAdmin)
            return new(WorkflowResultStatus.Forbidden, Error: "Only System Admin can manage workflow templates.");
        var template = await Active().SingleOrDefaultAsync(cancellationToken);
        return template is null ? new(WorkflowResultStatus.NotFound, Error: "No active Ticket workflow template is configured.")
            : new(WorkflowResultStatus.Success, await SnapshotAsync(template, cancellationToken));
    }

    public Task<WorkflowResult<WorkflowTemplateSnapshot>> InitializeDefaultAsync(CancellationToken cancellationToken = default) =>
        WorkflowTransactions.RunAsync<WorkflowTemplateSnapshot>(db, async () =>
        {
            var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
            if (actor?.Role != ApplicationRoles.SystemAdmin)
                return new(WorkflowResultStatus.Forbidden, Error: "Only System Admin can initialize the default workflow.");
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=N'HelpDesk.DefaultWorkflow',
                    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51000, 'Workflow setup is busy. Try again.', 1;
                """, cancellationToken);
            var active = await Active().SingleOrDefaultAsync(cancellationToken);
            if (active is not null) return new(WorkflowResultStatus.Success, await SnapshotAsync(active, cancellationToken));
            if (await db.WorkflowProcessTemplates.AnyAsync(x => x.Code == "HELPDESK_TICKET" && x.Version == 1, cancellationToken))
                return new(WorkflowResultStatus.Conflict, Error: "The default template already exists but is inactive. Existing configuration was preserved.");

            var template = new WorkflowProcessTemplate
            {
                Code = "HELPDESK_TICKET", Name = "IT Helpdesk Workflow", EntityType = TicketWorkflow.EntityType,
                Version = 1, IsPublished = true, IsActive = true, CreatedByUserId = actor.Id, PublishedAtUtc = DateTimeOffset.UtcNow
            };
            db.WorkflowProcessTemplates.Add(template);
            await db.SaveChangesAsync(cancellationToken);
            (string Code, string Name)[] definitions = [(TicketWorkflow.Open, "Open"), (TicketWorkflow.InProgress, "In Progress"),
                (TicketWorkflow.Resolved, "Resolved"), (TicketWorkflow.Closed, "Closed")];
            var steps = definitions.Select((x, index) => new WorkflowStepTemplate
            {
                ProcessTemplateId = template.Id, Code = x.Code, Name = x.Name, DisplayOrder = index,
                IsInitial = x.Code == TicketWorkflow.Open, IsTerminal = x.Code == TicketWorkflow.Closed
            }).ToArray();
            db.WorkflowStepTemplates.AddRange(steps);
            await db.SaveChangesAsync(cancellationToken);
            var byCode = steps.ToDictionary(x => x.Code);
            (string Code, string Name, string From, string To, WorkflowActionerType Actor)[] transitions =
            [
                (TicketWorkflow.StartProcessing, "Start Processing", TicketWorkflow.Open, TicketWorkflow.InProgress, WorkflowActionerType.CurrentAssignee),
                (TicketWorkflow.Resolve, "Resolve", TicketWorkflow.InProgress, TicketWorkflow.Resolved, WorkflowActionerType.CurrentAssignee),
                (TicketWorkflow.Close, "Close", TicketWorkflow.Resolved, TicketWorkflow.Closed, WorkflowActionerType.Requester),
                (TicketWorkflow.Reopen, "Reopen", TicketWorkflow.Resolved, TicketWorkflow.InProgress, WorkflowActionerType.Requester)
            ];
            var actions = transitions.Select(x => new WorkflowActionTemplate
            {
                ProcessTemplateId = template.Id, Code = x.Code, Name = x.Name, RequiresComment = TicketWorkflowPolicy.RequiresComment(x.Code, false),
                FromStepTemplateId = byCode[x.From].Id, ToStepTemplateId = byCode[x.To].Id
            }).ToArray();
            db.WorkflowActionTemplates.AddRange(actions);
            await db.SaveChangesAsync(cancellationToken);
            var actioners = actions.Select((action, index) => new WorkflowActionerTemplate
            {
                ActionTemplateId = action.Id, ActionerType = transitions[index].Actor
            }).ToArray();
            var error = TicketWorkflowPolicy.ValidateTemplate(steps, actions, actioners);
            if (error is not null) return new(WorkflowResultStatus.Invalid, Error: error);
            db.WorkflowActionerTemplates.AddRange(actioners);
            await db.SaveChangesAsync(cancellationToken);
            return new(WorkflowResultStatus.Success, await SnapshotAsync(template, cancellationToken));
        }, cancellationToken);

    private IQueryable<WorkflowProcessTemplate> Active() => db.WorkflowProcessTemplates.AsNoTracking()
        .Where(x => x.EntityType == TicketWorkflow.EntityType && x.IsPublished && x.IsActive);

    private async Task<WorkflowTemplateSnapshot> SnapshotAsync(WorkflowProcessTemplate template, CancellationToken ct)
    {
        var steps = await db.WorkflowStepTemplates.AsNoTracking().Where(x => x.ProcessTemplateId == template.Id)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var actions = await db.WorkflowActionTemplates.AsNoTracking().Where(x => x.ProcessTemplateId == template.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var ids = actions.Select(x => x.Id).ToArray();
        var actioners = await db.WorkflowActionerTemplates.AsNoTracking().Where(x => ids.Contains(x.ActionTemplateId)).OrderBy(x => x.Id).ToListAsync(ct);
        var byId = steps.ToDictionary(x => x.Id);
        return new(template.Id, template.Code, template.Name, template.Version,
            steps.Select(x => new WorkflowTemplateStep(x.Code, x.Name, x.DisplayOrder, x.IsInitial, x.IsTerminal)).ToArray(),
            actions.Select(x => new WorkflowTemplateAction(x.Code, x.Name, byId[x.FromStepTemplateId].Code,
                byId[x.ToStepTemplateId].Code, TicketWorkflowPolicy.RequiresComment(x.Code, x.RequiresComment),
                actioners.Where(a => a.ActionTemplateId == x.Id).Select(a => a.ActionerType.ToString()).ToArray())).ToArray());
    }
}
