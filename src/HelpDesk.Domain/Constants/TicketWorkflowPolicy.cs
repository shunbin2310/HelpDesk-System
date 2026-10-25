using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Constants;

public static class TicketWorkflowPolicy
{
    public static bool RequiresComment(string action, bool configured) => configured || action is TicketWorkflow.Resolve or TicketWorkflow.Reopen;
    public static bool CanView(Ticket ticket, int userId, string role) => role switch
    {
        ApplicationRoles.SystemAdmin or ApplicationRoles.SupportAdmin => true,
        ApplicationRoles.Employee => ticket.RequestedByUserId == userId,
        ApplicationRoles.SupportITStaff => ticket.AssignedToUserId == userId,
        _ => false
    };

    public static bool IsTransition(string from, string action, string to) => action switch
    {
        TicketWorkflow.StartProcessing => from == TicketWorkflow.Open && to == TicketWorkflow.InProgress,
        TicketWorkflow.Resolve => from == TicketWorkflow.InProgress && to == TicketWorkflow.Resolved,
        TicketWorkflow.Close => from == TicketWorkflow.Resolved && to == TicketWorkflow.Closed,
        TicketWorkflow.Reopen => from == TicketWorkflow.Resolved && to == TicketWorkflow.InProgress,
        _ => false
    };

    public static bool CanExecute(Ticket ticket, int userId, string role, string action,
        IEnumerable<WorkflowActionerInstance> actioners)
    {
        if (action is not (TicketWorkflow.StartProcessing or TicketWorkflow.Resolve or TicketWorkflow.Close or TicketWorkflow.Reopen))
            return false;
        if (role == ApplicationRoles.SystemAdmin)
            return true;
        // Template actioners can restrict permissions, but cannot widen Ticket business permissions.
        var permitted = action switch
        {
            TicketWorkflow.StartProcessing or TicketWorkflow.Resolve => role == ApplicationRoles.SupportITStaff && ticket.AssignedToUserId == userId,
            TicketWorkflow.Close or TicketWorkflow.Reopen => role == ApplicationRoles.Employee && ticket.RequestedByUserId == userId,
            _ => false
        };
        return permitted && actioners.Any(x => x.ActionerType switch
        {
            WorkflowActionerType.Requester => ticket.RequestedByUserId == userId && x.ActionerUserId == userId,
            WorkflowActionerType.CurrentAssignee => role == ApplicationRoles.SupportITStaff && ticket.AssignedToUserId == userId,
            WorkflowActionerType.Role => x.ActionerRole == role,
            WorkflowActionerType.User => x.ActionerUserId == userId,
            _ => false
        });
    }

    public static string? ValidateTemplate(IReadOnlyList<WorkflowStepTemplate> steps,
        IReadOnlyList<WorkflowActionTemplate> actions, IReadOnlyList<WorkflowActionerTemplate> actioners)
    {
        string[] codes = [TicketWorkflow.Open, TicketWorkflow.InProgress, TicketWorkflow.Resolved, TicketWorkflow.Closed];
        if (steps.Count != 4 || !steps.Select(x => x.Code).Order().SequenceEqual(codes.Order())
            || steps.Any(x => x.IsInitial != (x.Code == TicketWorkflow.Open) || x.IsTerminal != (x.Code == TicketWorkflow.Closed)))
            return "A Ticket template requires Open, In Progress, Resolved and Closed, with Open initial and only Closed terminal.";
        if (steps.Select(x => x.Id).Distinct().Count() != steps.Count)
            return "Step identifiers must be unique.";
        var byId = steps.ToDictionary(x => x.Id);
        string[] actionCodes = [TicketWorkflow.StartProcessing, TicketWorkflow.Resolve, TicketWorkflow.Close, TicketWorkflow.Reopen];
        if (actions.Count != 4 || !actions.Select(x => x.Code).Order().SequenceEqual(actionCodes.Order()))
            return "A Ticket template requires Start Processing, Resolve, Close and Reopen.";
        foreach (var action in actions)
        {
            if (!byId.TryGetValue(action.FromStepTemplateId, out var from) || !byId.TryGetValue(action.ToStepTemplateId, out var to)
                || from.ProcessTemplateId != action.ProcessTemplateId || to.ProcessTemplateId != action.ProcessTemplateId
                || !IsTransition(from.Code, action.Code, to.Code))
                return "The template contains an invalid Ticket transition.";
            var rules = actioners.Where(x => x.ActionTemplateId == action.Id).ToArray();
            if (rules.Length == 0 || rules.Any(x => !ValidActioner(x)))
                return "Each action requires valid actioners.";
        }
        return null;
    }

    private static bool ValidActioner(WorkflowActionerTemplate rule) => rule.ActionerType switch
    {
        WorkflowActionerType.Requester or WorkflowActionerType.CurrentAssignee => rule.ActionerRole is null && rule.ActionerUserId is null,
        WorkflowActionerType.Role => rule.ActionerUserId is null && rule.ActionerRole is not null && ApplicationRoles.IsValid(rule.ActionerRole),
        WorkflowActionerType.User => rule.ActionerRole is null && rule.ActionerUserId > 0,
        _ => false
    };
}
