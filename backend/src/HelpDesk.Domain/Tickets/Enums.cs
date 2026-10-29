namespace HelpDesk.Domain.Tickets;

/// <summary>The only five states a requester ever sees.</summary>
public enum TicketStatus
{
    New,
    InProgress,
    WaitingOnRequester,
    Resolved,
    Closed,
}

public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent,
}

public enum SegmentStatus
{
    Open,
    Resolved,
}

public enum MessageVisibility
{
    /// <summary>Visible to the requester and every area.</summary>
    Public,

    /// <summary>Internal note visible to every area that works on the ticket.</summary>
    InternalAllAreas,

    /// <summary>Internal note visible only to the author's area.</summary>
    InternalOwnArea,
}

public enum MessageSource
{
    Portal,
    Email,
    System,
}

public enum TicketEventType
{
    Created,
    StatusChanged,
    Assigned,
    PriorityChanged,
    CategoryChanged,
    Escalated,
    SegmentResolved,
    Reopened,
    Commented,
    Rated,
}
