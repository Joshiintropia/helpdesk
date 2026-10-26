namespace HelpDesk.Domain.Tickets;

/// <summary>
/// An append-only record of something that happened to a ticket. Metrics (SLA, response and
/// resolution times, time per area) are computed from these rows, and they double as the audit log.
/// </summary>
public class TicketEvent
{
    private TicketEvent()
    {
    }

    internal TicketEvent(
        Ticket ticket,
        TicketEventType type,
        Guid? actorId,
        DateTimeOffset occurredAt,
        string? fromValue,
        string? toValue,
        int? areaId,
        Guid? segmentId)
    {
        Ticket = ticket;
        Type = type;
        ActorId = actorId;
        OccurredAt = occurredAt;
        FromValue = fromValue;
        ToValue = toValue;
        AreaId = areaId;
        SegmentId = segmentId;
    }

    public long Id { get; private set; }
    public long TicketId { get; private set; }
    public Ticket Ticket { get; private set; } = null!;
    public TicketEventType Type { get; private set; }

    /// <summary>Null when the system acted on its own (for example, auto-closing).</summary>
    public Guid? ActorId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
    public string? FromValue { get; private set; }
    public string? ToValue { get; private set; }
    public int? AreaId { get; private set; }
    public Guid? SegmentId { get; private set; }
}
