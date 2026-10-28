namespace HelpDesk.Domain.Tickets;

/// <summary>
/// A "tramo": the part of a ticket one area works on. Every ticket starts with a root segment for the
/// area that received it; each escalation opens a child segment for the next area in the chain.
/// Each segment has its own SLA, which is how the dashboard shows where a chain gets stuck.
/// </summary>
public class TicketSegment
{
    private TicketSegment()
    {
    }

    public Guid Id { get; private set; }
    public long TicketId { get; private set; }
    public Ticket Ticket { get; private set; } = null!;
    public int AreaId { get; private set; }

    /// <summary>The segment that escalated into this one; null for the root segment.</summary>
    public Guid? ParentSegmentId { get; private set; }

    public Guid? EscalatedById { get; private set; }
    public string? EscalationReason { get; private set; }
    public Guid? AssigneeId { get; internal set; }
    public SegmentStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolutionNote { get; private set; }

    /// <summary>When this area first acted on the segment: a message, an escalation or a resolution.</summary>
    public DateTimeOffset? FirstRespondedAt { get; private set; }

    /// <summary>The SLA policy the due dates come from; null when no policy applies.</summary>
    public int? SlaPolicyId { get; private set; }

    public DateTimeOffset? FirstResponseDueAt { get; private set; }
    public DateTimeOffset? ResolutionDueAt { get; private set; }

    /// <summary>
    /// Business minutes the clock was stopped: while waiting on the requester, while the ticket was resolved,
    /// and while another area worked on what this one escalated. Each area is measured only on its own time.
    /// </summary>
    public int SlaPausedMinutes { get; private set; }

    public void SetSlaTargets(int? policyId, DateTimeOffset? firstResponseDueAt, DateTimeOffset? resolutionDueAt)
    {
        SlaPolicyId = policyId;
        FirstResponseDueAt = firstResponseDueAt;
        ResolutionDueAt = resolutionDueAt;
    }

    public void AddSlaPause(int businessMinutes) => SlaPausedMinutes += Math.Max(0, businessMinutes);

    internal void MarkResponded(DateTimeOffset now) => FirstRespondedAt ??= now;

    internal static TicketSegment Start(
        Ticket ticket,
        int areaId,
        TicketSegment? parent,
        Guid? escalatedById,
        string? reason,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            Ticket = ticket,
            AreaId = areaId,
            ParentSegmentId = parent?.Id,
            EscalatedById = escalatedById,
            EscalationReason = reason,
            Status = SegmentStatus.Open,
            OpenedAt = now,
        };

    internal void Resolve(string? note, DateTimeOffset now)
    {
        MarkResponded(now);
        Status = SegmentStatus.Resolved;
        ResolvedAt = now;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    internal void Reopen()
    {
        Status = SegmentStatus.Open;
        ResolvedAt = null;
        ResolutionNote = null;
    }
}
