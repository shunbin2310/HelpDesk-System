using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Workflows;

public sealed class WorkflowStepInstance : Entity
{
    public long ProcessInstanceId
    {
        get; set;
    }
    public long SourceStepTemplateId
    {
        get; set;
    }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder
    {
        get; set;
    }
    public bool IsInitial
    {
        get; set;
    }
    public bool IsTerminal
    {
        get; set;
    }
}
