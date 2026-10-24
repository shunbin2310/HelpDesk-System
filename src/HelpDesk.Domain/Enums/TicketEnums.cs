namespace HelpDesk.Domain.Enums;

public enum TicketCategory : byte { Hardware = 1, Software, Network, Account, Other }
public enum TicketPriority : byte { Low = 1, Medium, High, Critical }
public enum TicketMessageType : byte { PublicReply = 1, InternalNote }
public enum TicketHistoryEventType { Assigned, Reassigned, PriorityChanged, Delegated, DelegationReturned }
