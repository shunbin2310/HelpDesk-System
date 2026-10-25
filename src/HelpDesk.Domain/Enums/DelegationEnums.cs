namespace HelpDesk.Domain.Enums;

public enum DelegationEntryReason
{
    ExistingTicket, NewAssignment
}
public enum DelegationReturnOutcome
{
    AwaitingEnd, Returned, SkippedClosed, SkippedReassigned
}
