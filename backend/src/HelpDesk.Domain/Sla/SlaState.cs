using HelpDesk.Domain.Tickets;

namespace HelpDesk.Domain.Sla;

public enum SlaTarget
{
    FirstResponse,
    Resolution,
}

public enum SlaState
{
    /// <summary>No policy applies to this segment.</summary>
    None,
    OnTrack,

    /// <summary>Less than a quarter of the time is left.</summary>
    AtRisk,
    Breached,

    /// <summary>The clock is stopped: waiting on the requester, or the ticket is resolved.</summary>
    Paused,

    /// <summary>The segment was resolved within its targets.</summary>
    Met,
}

/// <summary>Where one segment stands against its SLA right now.</summary>
public sealed record SlaStatus(SlaState State, SlaTarget? Target, DateTimeOffset? DueAt)
{
    public static SlaStatus For(TicketSegment segment, Ticket ticket, DateTimeOffset now)
    {
        if (segment.ResolutionDueAt is null)
        {
            return new SlaStatus(SlaState.None, null, null);
        }

        if (segment.Status == SegmentStatus.Resolved)
        {
            var responseLate = segment.FirstResponseDueAt is { } responseDue && (segment.FirstRespondedAt ?? segment.ResolvedAt) > responseDue;
            var resolutionLate = segment.ResolvedAt > segment.ResolutionDueAt;
            return resolutionLate || responseLate
                ? new SlaStatus(SlaState.Breached, resolutionLate ? SlaTarget.Resolution : SlaTarget.FirstResponse, resolutionLate ? segment.ResolutionDueAt : segment.FirstResponseDueAt)
                : new SlaStatus(SlaState.Met, SlaTarget.Resolution, segment.ResolutionDueAt);
        }

        var (target, due) = segment.FirstRespondedAt is null && segment.FirstResponseDueAt is { } firstDue
            ? (SlaTarget.FirstResponse, firstDue)
            : (SlaTarget.Resolution, segment.ResolutionDueAt.Value);

        // While paused, the due date moves later when the clock restarts; it only counts as late if it already was.
        if (ticket.SlaPausedAt is { } pausedAt)
        {
            return new SlaStatus(pausedAt > due ? SlaState.Breached : SlaState.Paused, target, due);
        }

        if (now > due)
        {
            return new SlaStatus(SlaState.Breached, target, due);
        }

        var total = due - segment.OpenedAt;
        var left = due - now;
        return new SlaStatus(left < total / 4 ? SlaState.AtRisk : SlaState.OnTrack, target, due);
    }
}
