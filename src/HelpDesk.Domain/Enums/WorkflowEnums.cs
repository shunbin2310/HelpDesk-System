namespace HelpDesk.Domain.Enums;

public enum WorkflowActionerType : byte { Requester = 1, CurrentAssignee, Role, User }
public enum WorkflowInstanceStatus : byte { Running = 1, Completed }
