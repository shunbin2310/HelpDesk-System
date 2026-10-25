namespace HelpDesk.Application.Workflows;

public enum WorkflowResultStatus
{
    Success, NotFound, Forbidden, Conflict, Invalid
}
public sealed record WorkflowResult<T>(WorkflowResultStatus Status, T? Value = default, string? Error = null);
public sealed record WorkflowActionOption(string Code, string Name, bool RequiresComment);
public sealed record WorkflowHistoryItem(string? FromStepCode, string ToStepCode, string ActionCode,
    int ActorUserId, string ActorName, string? Comment, DateTimeOffset ActionAtUtc);
public sealed record WorkflowSnapshot(long Id, long TicketId, string TemplateCode, int TemplateVersion,
    string CurrentStepCode, string Status, byte[] RowVersion, IReadOnlyList<WorkflowActionOption> AvailableActions,
    IReadOnlyList<WorkflowHistoryItem> History);
public sealed record ExecuteWorkflowAction(string ActionCode, byte[] RowVersion, string? Comment = null);

public interface IWorkflowEngine
{
    // Save the new Ticket first, inside the caller's creation transaction; roll that transaction back on any failure.
    Task<WorkflowResult<WorkflowSnapshot>> StartAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<WorkflowResult<WorkflowSnapshot>> GetAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<WorkflowResult<WorkflowSnapshot>> ExecuteAsync(long ticketId, ExecuteWorkflowAction request, CancellationToken cancellationToken = default);
}

public sealed record WorkflowTemplateStep(string Code, string Name, int DisplayOrder, bool IsInitial, bool IsTerminal);
public sealed record WorkflowTemplateAction(string Code, string Name, string FromStepCode, string ToStepCode,
    bool RequiresComment, IReadOnlyList<string> Actioners);
public sealed record WorkflowTemplateSnapshot(long Id, string Code, string Name, int Version,
    IReadOnlyList<WorkflowTemplateStep> Steps, IReadOnlyList<WorkflowTemplateAction> Actions);

public interface IWorkflowTemplateService
{
    Task<WorkflowResult<WorkflowTemplateSnapshot>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<WorkflowResult<WorkflowTemplateSnapshot>> InitializeDefaultAsync(CancellationToken cancellationToken = default);
}
