using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Workflows;

public sealed class WorkflowProcessInstance : VersionedEntity
{
    public long TicketId
    {
        get; set;
    }
    public long ProcessTemplateId
    {
        get; set;
    }
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public int TemplateVersion
    {
        get; set;
    }
    // Nullable only to insert the process and its steps in one creation transaction.
    public long? CurrentStepInstanceId
    {
        get; set;
    }
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc
    {
        get; set;
    }
}
