using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Workflows;

public sealed class WorkflowActionInstance : Entity
{
    public long ProcessInstanceId
    {
        get; set;
    }
    public long FromStepInstanceId
    {
        get; set;
    }
    public long ToStepInstanceId
    {
        get; set;
    }
    public long SourceActionTemplateId
    {
        get; set;
    }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool RequiresComment
    {
        get; set;
    }
}
