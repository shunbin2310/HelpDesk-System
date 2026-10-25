using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Workflows;

public sealed class WorkflowStepTemplate : Entity
{
    public long ProcessTemplateId
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
