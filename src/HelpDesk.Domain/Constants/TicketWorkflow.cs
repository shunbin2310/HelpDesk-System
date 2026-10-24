namespace HelpDesk.Domain.Constants;

public static class TicketWorkflow
{
    public const string EntityType = "Ticket";
    public const string Open = "OPEN";
    public const string InProgress = "IN_PROGRESS";
    public const string Resolved = "RESOLVED";
    public const string Closed = "CLOSED";

    public const string StartProcessing = "START_PROCESSING";
    public const string Resolve = "RESOLVE";
    public const string Close = "CLOSE";
    public const string Reopen = "REOPEN";
}
