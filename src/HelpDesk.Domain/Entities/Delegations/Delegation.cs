using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Delegations;

public sealed class Delegation : VersionedEntity
{
    public int SourceUserId
    {
        get; set;
    }
    public int TargetUserId
    {
        get; set;
    }
    public DateTimeOffset StartsAtUtc
    {
        get; set;
    }
    public DateTimeOffset EndsAtUtc
    {
        get; set;
    }
    public string? Reason
    {
        get; set;
    }
    public int CreatedByUserId
    {
        get; set;
    }
    public DateTimeOffset? ActivatedAtUtc
    {
        get; set;
    }
    public DateTimeOffset? CompletedAtUtc
    {
        get; set;
    }
    public DateTimeOffset? StoppedAtUtc
    {
        get; set;
    }
    public int? StoppedByUserId
    {
        get; set;
    }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
