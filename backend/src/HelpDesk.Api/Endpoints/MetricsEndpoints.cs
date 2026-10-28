using HelpDesk.Api.Auth;
using HelpDesk.Domain.Sla;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Endpoints;

/// <summary>
/// The dashboard: volume, SLA compliance and time per area, and where tickets travel between areas.
/// Agents see their own areas; supervisors and administrators see all of them. Times are business hours,
/// the same ones SLAs are measured in.
/// </summary>
public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/metrics", Get).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Get(
        CurrentUser me,
        HelpDeskDbContext db,
        TimeProvider clock,
        int days = 30,
        int? areaId = null,
        CancellationToken ct = default)
    {
        var scope = await me.AreaIdsAsync(ct);
        if (scope.Count == 0)
        {
            return TypedResults.Forbid();
        }

        if (areaId is { } only)
        {
            if (!scope.Contains(only))
            {
                return TypedResults.Forbid();
            }

            scope = [only];
        }

        days = Math.Clamp(days, 1, 366);
        var now = clock.GetUtcNow();
        var calendar = await db.BusinessCalendars.AsNoTracking().Include(c => c.Holidays).OrderBy(c => c.Id).FirstAsync(ct);
        var time = new BusinessTime(calendar);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var firstDay = today.AddDays(1 - days);
        var from = LocalMidnightUtc(firstDay, zone);

        // Every ticket that touches the areas in scope and was active in the period or is still open.
        var tickets = await db.Tickets
            .AsNoTracking()
            .Include(t => t.Segments)
            .Where(t => t.Segments.Any(s => scope.Contains(s.AreaId)))
            .Where(t => t.CreatedAt >= from || t.ResolvedAt >= from || (t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed))
            .AsSplitQuery()
            .ToListAsync(ct);

        var areaNames = await db.Areas.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var segments = tickets.SelectMany(t => t.Segments.Select(s => (Ticket: t, Segment: s))).ToList();
        var inScope = segments.Where(x => scope.Contains(x.Segment.AreaId)).ToList();

        bool Open(Ticket t) => t.Status is not (TicketStatus.Resolved or TicketStatus.Closed);
        bool IsActive((Ticket Ticket, TicketSegment Segment) x) =>
            x.Segment.Status == SegmentStatus.Open
            && !x.Ticket.Segments.Any(c => c.ParentSegmentId == x.Segment.Id && c.Status == SegmentStatus.Open);

        var totals = new MetricsTotalsDto(
            Received: tickets.Count(t => t.CreatedAt >= from),
            Resolved: tickets.Count(t => t.ResolvedAt >= from),
            Open: tickets.Count(Open),
            Overdue: inScope.Count(x => IsActive(x) && SlaStatus.For(x.Segment, x.Ticket, now).State == SlaState.Breached),
            Sla: Compliance(inScope.Select(x => x.Segment), from, now, time));

        var daily = Enumerable.Range(0, days)
            .Select(i => firstDay.AddDays(i))
            .Select(day => new DailyCountDto(
                day,
                tickets.Count(t => t.CreatedAt >= from && LocalDay(t.CreatedAt, zone) == day),
                tickets.Count(t => t.ResolvedAt is { } r && r >= from && LocalDay(r, zone) == day)))
            .ToList();

        var areas = inScope
            .GroupBy(x => x.Segment.AreaId)
            .Select(g =>
            {
                var backlog = g.Where(IsActive).Select(x => SlaStatus.For(x.Segment, x.Ticket, now).State).ToList();
                return new AreaMetricsDto(
                    g.Key,
                    areaNames.GetValueOrDefault(g.Key, ""),
                    Received: g.Count(x => x.Segment.OpenedAt >= from),
                    Resolved: g.Count(x => x.Segment.ResolvedAt >= from),
                    Open: g.Count(x => x.Segment.Status == SegmentStatus.Open),
                    EscalatedOut: segments.Count(c => c.Segment.OpenedAt >= from && g.Any(p => p.Segment.Id == c.Segment.ParentSegmentId)),
                    Sla: Compliance(g.Select(x => x.Segment), from, now, time),
                    Backlog: new BacklogDto(
                        backlog.Count(s => s == SlaState.OnTrack),
                        backlog.Count(s => s == SlaState.AtRisk),
                        backlog.Count(s => s == SlaState.Breached),
                        backlog.Count(s => s == SlaState.Paused),
                        backlog.Count(s => s == SlaState.None)));
            })
            .OrderByDescending(a => a.Received)
            .ThenBy(a => a.Name)
            .ToList();

        var byId = segments.ToDictionary(x => x.Segment.Id, x => x.Segment);
        var escalations = segments
            .Where(x => x.Segment.OpenedAt >= from && x.Segment.ParentSegmentId is { } parent && byId.ContainsKey(parent))
            .Select(x => (From: byId[x.Segment.ParentSegmentId!.Value].AreaId, To: x.Segment.AreaId))
            .Where(e => scope.Contains(e.From) || scope.Contains(e.To))
            .GroupBy(e => e)
            .Select(g => new EscalationFlowDto(g.Key.From, areaNames.GetValueOrDefault(g.Key.From, ""), g.Key.To, areaNames.GetValueOrDefault(g.Key.To, ""), g.Count()))
            .OrderByDescending(e => e.Count)
            .ToList();

        return TypedResults.Ok(new MetricsDto(firstDay, today, calendar.TimeZone, totals, daily, areas, escalations));
    }

    /// <summary>
    /// First response: segments opened in the period whose target has been decided (answered, or past due).
    /// Resolution: segments resolved in the period, plus open ones already past due.
    /// </summary>
    private static SlaComplianceDto Compliance(IEnumerable<TicketSegment> segments, DateTimeOffset from, DateTimeOffset now, BusinessTime time)
    {
        var list = segments.Where(s => s.ResolutionDueAt is not null).ToList();

        var firstResponse = list
            .Where(s => s.OpenedAt >= from && s.FirstResponseDueAt is { } due && (s.FirstRespondedAt is not null || now > due))
            .ToList();
        var resolution = list
            .Where(s => s.ResolvedAt >= from || (s.Status == SegmentStatus.Open && now > s.ResolutionDueAt && s.Ticket.SlaPausedAt is null))
            .ToList();

        var responseMinutes = list
            .Where(s => s.OpenedAt >= from && s.FirstRespondedAt is not null)
            .Select(s => time.MinutesBetween(s.OpenedAt, s.FirstRespondedAt!.Value))
            .ToList();
        var resolutionMinutes = list
            .Where(s => s.ResolvedAt >= from)
            .Select(s => Math.Max(0, time.MinutesBetween(s.OpenedAt, s.ResolvedAt!.Value) - s.SlaPausedMinutes))
            .ToList();

        return new SlaComplianceDto(
            FirstResponseMet: firstResponse.Count(s => s.FirstRespondedAt is { } at && at <= s.FirstResponseDueAt),
            FirstResponseTotal: firstResponse.Count,
            ResolutionMet: resolution.Count(s => s.ResolvedAt is { } at && at <= s.ResolutionDueAt),
            ResolutionTotal: resolution.Count,
            MedianFirstResponseMinutes: Median(responseMinutes),
            MedianResolutionMinutes: Median(resolutionMinutes));
    }

    private static int? Median(List<int> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    private static DateOnly LocalDay(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    private static DateTimeOffset LocalMidnightUtc(DateOnly day, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);
}
