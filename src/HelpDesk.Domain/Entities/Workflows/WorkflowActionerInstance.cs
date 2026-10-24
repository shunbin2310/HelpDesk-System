using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Workflows;

public sealed class WorkflowActionerInstance : Entity
{
    public long ActionInstanceId { get; set; }
    public long SourceActionerTemplateId { get; set; }
    public WorkflowActionerType ActionerType { get; set; }
    public string? ActionerRole { get; set; }
    public int? ActionerUserId { get; set; }
}
