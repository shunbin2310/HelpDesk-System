using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Domain.Enums;

namespace HelpDesk.UnitTests;

public sealed class TicketWorkflowPolicyTests
{
    [Theory]
    [InlineData("OPEN", "START_PROCESSING", "IN_PROGRESS", true)]
    [InlineData("IN_PROGRESS", "RESOLVE", "RESOLVED", true)]
    [InlineData("RESOLVED", "CLOSE", "CLOSED", true)]
    [InlineData("RESOLVED", "REOPEN", "IN_PROGRESS", true)]
    [InlineData("OPEN", "CLOSE", "CLOSED", false)]
    [InlineData("RESOLVED", "RESOLVE", "RESOLVED", false)]
    [InlineData("CLOSED", "REOPEN", "IN_PROGRESS", false)]
    [InlineData("OPEN", "SKIP", "RESOLVED", false)]
    public void OnlyTheFourAgreedTransitionsAreValid(string from, string action, string to, bool expected) =>
        Assert.Equal(expected, TicketWorkflowPolicy.IsTransition(from, action, to));

    [Theory]
    [InlineData(10, "EMPLOYEE", "CLOSE", true)]
    [InlineData(11, "EMPLOYEE", "CLOSE", false)]
    [InlineData(10, "EMPLOYEE", "REOPEN", true)]
    [InlineData(20, "SUPPORT_IT_STAFF", "START_PROCESSING", true)]
    [InlineData(20, "SUPPORT_IT_STAFF", "RESOLVE", true)]
    [InlineData(21, "SUPPORT_IT_STAFF", "RESOLVE", false)]
    [InlineData(20, "SUPPORT_IT_STAFF", "CLOSE", false)]
    [InlineData(10, "EMPLOYEE", "RESOLVE", false)]
    [InlineData(30, "SUPPORT_ADMIN", "RESOLVE", false)]
    [InlineData(30, "SUPPORT_ADMIN", "CLOSE", false)]
    [InlineData(40, "SYSTEM_ADMIN", "RESOLVE", true)]
    [InlineData(40, "SYSTEM_ADMIN", "CLOSE", true)]
    [InlineData(40, "SYSTEM_ADMIN", "UNKNOWN", false)]
    public void ActionerRoleOrUserCannotWidenBusinessPermissions(int userId, string role, string action, bool expected)
    {
        var ticket = new Ticket { RequestedByUserId = 10, AssignedToUserId = 20 };
        WorkflowActionerInstance[] rules = [new() { ActionerType = WorkflowActionerType.Role, ActionerRole = role },
            new() { ActionerType = WorkflowActionerType.User, ActionerUserId = userId }];
        Assert.Equal(expected, TicketWorkflowPolicy.CanExecute(ticket, userId, role, action, rules));
    }

    [Fact]
    public void AssigneeIsDynamicAndAStaffRoleDoesNotAllowUnassignedTickets()
    {
        var ticket = new Ticket { RequestedByUserId = 10, AssignedToUserId = 20 };
        WorkflowActionerInstance[] rules = [new() { ActionerType = WorkflowActionerType.CurrentAssignee }];
        Assert.True(TicketWorkflowPolicy.CanExecute(ticket, 20, ApplicationRoles.SupportITStaff, TicketWorkflow.Resolve, rules));
        ticket.AssignedToUserId = 21;
        Assert.False(TicketWorkflowPolicy.CanView(ticket, 20, ApplicationRoles.SupportITStaff));
        Assert.False(TicketWorkflowPolicy.CanExecute(ticket, 20, ApplicationRoles.SupportITStaff, TicketWorkflow.Resolve, rules));
        Assert.True(TicketWorkflowPolicy.CanExecute(ticket, 21, ApplicationRoles.SupportITStaff, TicketWorkflow.Resolve, rules));
        ticket.AssignedToUserId = null;
        Assert.False(TicketWorkflowPolicy.CanExecute(ticket, 21, ApplicationRoles.SupportITStaff, TicketWorkflow.StartProcessing, rules));
    }

    [Fact]
    public void EmptyActionersDoNotMakeActionsPublic()
    {
        var ticket = new Ticket { RequestedByUserId = 10, AssignedToUserId = 20 };
        Assert.False(TicketWorkflowPolicy.CanExecute(ticket, 20, ApplicationRoles.SupportITStaff, TicketWorkflow.Resolve, []));
        Assert.True(TicketWorkflowPolicy.CanExecute(ticket, 40, ApplicationRoles.SystemAdmin, TicketWorkflow.Resolve, []));
    }

    [Fact]
    public void ResolvedCannotBeConfiguredAsATerminalStep()
    {
        var steps = new[] { TicketWorkflow.Open, TicketWorkflow.InProgress, TicketWorkflow.Resolved, TicketWorkflow.Closed }
            .Select((code, index) => new WorkflowStepTemplate
            {
                Id = index + 1,
                ProcessTemplateId = 1,
                Code = code,
                IsInitial = code == TicketWorkflow.Open,
                IsTerminal = code == TicketWorkflow.Closed
            }).ToArray();
        var actions = new[] { (TicketWorkflow.StartProcessing, 1, 2), (TicketWorkflow.Resolve, 2, 3),
                (TicketWorkflow.Close, 3, 4), (TicketWorkflow.Reopen, 3, 2) }
            .Select((value, index) => new WorkflowActionTemplate
            {
                Id = index + 1,
                ProcessTemplateId = 1,
                Code = value.Item1,
                FromStepTemplateId = value.Item2,
                ToStepTemplateId = value.Item3
            }).ToArray();
        var rules = actions.Select(x => new WorkflowActionerTemplate
        {
            ActionTemplateId = x.Id,
            ActionerType = x.Code is TicketWorkflow.Close or TicketWorkflow.Reopen ? WorkflowActionerType.Requester : WorkflowActionerType.CurrentAssignee
        }).ToArray();
        Assert.Null(TicketWorkflowPolicy.ValidateTemplate(steps, actions, rules));
        steps[2].IsTerminal = true;
        Assert.NotNull(TicketWorkflowPolicy.ValidateTemplate(steps, actions, rules));
    }
}
