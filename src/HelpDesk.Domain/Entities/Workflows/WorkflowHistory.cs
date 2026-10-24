using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Workflows;

public sealed class WorkflowHistory : Entity
{
    public long ProcessInstanceId { get; set; }
    public long? ActionInstanceId { get; set; }
    public string? FromStepCode { get; set; }
    public string ToStepCode { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public int ActionByUserId { get; set; }
    public string ActionByNameSnapshot { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTimeOffset ActionAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
