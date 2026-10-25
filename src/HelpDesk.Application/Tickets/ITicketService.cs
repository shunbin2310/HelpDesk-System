using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Application.Tickets;

public enum TicketResultStatus { Success, NotFound, Forbidden, Conflict, Invalid }
public sealed record TicketResult<T>(TicketResultStatus Status, T? Value = default, string? Error = null);
public sealed record CreateTicketRequest(string Title, string Description, TicketCategory Category);
public sealed record AssignTicketRequest(int AssignedToUserId, byte[] RowVersion);
public sealed record SetTicketPriorityRequest(TicketPriority Priority, byte[] RowVersion);
public sealed record TicketAssignee(int Id, string FullName);
public sealed record TicketListItem
{
    public long Id { get; init; }
    public string TicketNumber { get; init; } = "";
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public string Priority { get; init; } = "";
    public string Status { get; init; } = "";
    public int RequestedByUserId { get; init; }
    public string RequesterName { get; init; } = "";
    public int? AssignedToUserId { get; init; }
    public string? AssigneeName { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
public sealed record TicketPage(IReadOnlyList<TicketListItem> Items, int Total, int Page);
public sealed record TicketHistoryItem(long Id, string EventType, string ActorName, string? OldAssigneeName,
    string? NewAssigneeName, string? OldPriority, string? NewPriority, DateTimeOffset CreatedAtUtc);
public sealed record TicketDetail(TicketListItem Ticket, string Description, byte[] RowVersion,
    WorkflowSnapshot Workflow, IReadOnlyList<TicketHistoryItem> ManagementHistory);

public interface ITicketService
{
    Task<TicketResult<TicketPage>> ListAsync(int page, string? status, bool onlyUnassigned, CancellationToken cancellationToken = default);
    Task<TicketResult<TicketDetail>> GetAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<TicketResult<TicketDetail>> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken = default);
    Task<TicketResult<TicketDetail>> CreateAsync(CreateTicketRequest request, IReadOnlyList<AttachmentUpload> uploads, CancellationToken cancellationToken = default);
    Task<TicketResult<IReadOnlyList<TicketAssignee>>> GetAssigneesAsync(CancellationToken cancellationToken = default);
    Task<TicketResult<TicketDetail>> AssignAsync(long ticketId, AssignTicketRequest request, CancellationToken cancellationToken = default);
    Task<TicketResult<TicketDetail>> SetPriorityAsync(long ticketId, SetTicketPriorityRequest request, CancellationToken cancellationToken = default);
}
