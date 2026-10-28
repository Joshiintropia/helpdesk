using HelpDesk.Domain.Sla;
using HelpDesk.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HelpDesk.Infrastructure.Sla;

/// <summary>
/// Keeps every segment's SLA due dates current as tickets are saved: sets them when a segment opens,
/// recalculates them when the priority changes, and pushes them later by the business time the clock was
/// stopped. Doing it here means the web, the API and email all follow the same rules.
/// </summary>
public sealed class SlaInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Apply(context, LoadRules(context, Pending(context).Any()));
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var needed = Pending(context).Any();
            var rules = needed ? await LoadRulesAsync(context, cancellationToken) : SlaRules.Empty;
            Apply(context, rules);
        }

        return result;
    }

    /// <summary>The ticket changes that move due dates; empty for most saves, which then skip loading the rules.</summary>
    private static IEnumerable<object> Pending(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries<TicketSegment>())
        {
            if (entry.State == EntityState.Added
                || (entry.State == EntityState.Modified && entry.Property(s => s.Status).IsModified && entry.Entity.ParentSegmentId is not null))
            {
                yield return entry;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<Ticket>())
        {
            if (entry.State == EntityState.Modified
                && (entry.Property(t => t.Priority).IsModified || entry.Property(t => t.SlaPausedAt).IsModified))
            {
                yield return entry;
            }
        }
    }

    private void Apply(DbContext context, SlaRules rules)
    {
        if (rules.IsEmpty)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var tickets = new HashSet<Ticket>(ReferenceEqualityComparer.Instance);
        var recalculate = new HashSet<TicketSegment>(ReferenceEqualityComparer.Instance);

        foreach (var entry in context.ChangeTracker.Entries<TicketSegment>().ToList())
        {
            var segment = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                recalculate.Add(segment);
            }
            else if (entry.State == EntityState.Modified
                && entry.Property(s => s.Status).IsModified
                && segment.Status == SegmentStatus.Resolved
                && segment.ResolvedAt is { } resolvedAt
                && segment.Ticket.Segments.SingleOrDefault(s => s.Id == segment.ParentSegmentId) is { Status: SegmentStatus.Open } parent)
            {
                // The escalating area waited while this one worked; that time is not held against it.
                if (rules.TimeFor(parent, segment.Ticket) is { } time)
                {
                    parent.AddSlaPause(time.MinutesBetween(segment.OpenedAt, resolvedAt));
                    recalculate.Add(parent);
                }
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<Ticket>().Where(e => e.State == EntityState.Modified))
        {
            var ticket = entry.Entity;
            var pause = entry.Property(t => t.SlaPausedAt);
            if (pause.IsModified && pause.OriginalValue is { } pausedAt && ticket.SlaPausedAt is null)
            {
                // The clock restarts: the areas still working get the stopped time back.
                foreach (var segment in ActiveSegments(ticket))
                {
                    if (rules.TimeFor(segment, ticket) is { } time)
                    {
                        segment.AddSlaPause(time.MinutesBetween(pausedAt, now));
                        recalculate.Add(segment);
                    }
                }
            }

            if (entry.Property(t => t.Priority).IsModified)
            {
                tickets.Add(ticket);
            }
        }

        foreach (var segment in tickets.SelectMany(t => t.Segments).Where(s => s.Status == SegmentStatus.Open))
        {
            recalculate.Add(segment);
        }

        foreach (var segment in recalculate)
        {
            rules.Recalculate(segment, segment.Ticket);
        }
    }

    /// <summary>Open segments that are not waiting on another area.</summary>
    private static IEnumerable<TicketSegment> ActiveSegments(Ticket ticket) =>
        ticket.Segments.Where(s => s.Status == SegmentStatus.Open
            && !ticket.Segments.Any(child => child.ParentSegmentId == s.Id && child.Status == SegmentStatus.Open));

    private static SlaRules LoadRules(DbContext context, bool needed) =>
        needed
            ? new SlaRules(
                context.Set<SlaPolicy>().AsNoTracking().ToList(),
                context.Set<BusinessCalendar>().AsNoTracking().Include(c => c.Holidays).ToList())
            : SlaRules.Empty;

    private static async Task<SlaRules> LoadRulesAsync(DbContext context, CancellationToken cancellationToken) =>
        new(
            await context.Set<SlaPolicy>().AsNoTracking().ToListAsync(cancellationToken),
            await context.Set<BusinessCalendar>().AsNoTracking().Include(c => c.Holidays).ToListAsync(cancellationToken));
}

/// <summary>The policies and calendars in effect, and how they turn into due dates.</summary>
internal sealed class SlaRules(IReadOnlyList<SlaPolicy> policies, IReadOnlyList<BusinessCalendar> calendars)
{
    public static readonly SlaRules Empty = new([], []);

    private readonly Dictionary<int, BusinessTime> _times = calendars.ToDictionary(c => c.Id, c => new BusinessTime(c));

    public bool IsEmpty => policies.Count == 0;

    /// <summary>The area's own policy for the priority, else the general one; null when neither exists.</summary>
    public SlaPolicy? PolicyFor(int areaId, TicketPriority priority) =>
        policies.FirstOrDefault(p => p.AreaId == areaId && p.Priority == priority)
        ?? policies.FirstOrDefault(p => p.AreaId == null && p.Priority == priority);

    public BusinessTime? TimeFor(TicketSegment segment, Ticket ticket) =>
        PolicyFor(segment.AreaId, ticket.Priority) is { } policy ? _times.GetValueOrDefault(policy.CalendarId) : null;

    public void Recalculate(TicketSegment segment, Ticket ticket)
    {
        if (PolicyFor(segment.AreaId, ticket.Priority) is not { } policy || !_times.TryGetValue(policy.CalendarId, out var time))
        {
            segment.SetSlaTargets(null, null, null);
            return;
        }

        segment.SetSlaTargets(
            policy.Id,
            time.Add(segment.OpenedAt, policy.FirstResponseMinutes + segment.SlaPausedMinutes),
            time.Add(segment.OpenedAt, policy.ResolutionMinutes + segment.SlaPausedMinutes));
    }
}
