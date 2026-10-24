using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Workflows;

public sealed class WorkflowActionTemplate : Entity
{
    public long ProcessTemplateId { get; set; }
    public long FromStepTemplateId { get; set; }
    public long ToStepTemplateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool RequiresComment { get; set; }
}
