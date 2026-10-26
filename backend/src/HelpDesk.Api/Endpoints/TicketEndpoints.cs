using HelpDesk.Api.Auth;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Endpoints;

public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var tickets = app.MapGroup("/api/tickets").RequireAuthorization();

        // Requester
        tickets.MapPost("/", Create);
        tickets.MapGet("/mine", ListMine);
        tickets.MapGet("/{id:long}", Get);
        tickets.MapPost("/{id:long}/messages", AddMessage);
        tickets.MapPost("/{id:long}/reopen", Reopen);

        // Agents
        tickets.MapGet("/queue", Queue);
        tickets.MapPost("/{id:long}/escalate", Escalate);
        tickets.MapPost("/{id:long}/segments/{segmentId:guid}/resolve", ResolveSegment);
        tickets.MapPost("/{id:long}/resolve", ResolveAll);
        return app;
    }

    private static async Task<IResult> Create(
        CreateTicketRequest request,
        CurrentUser me,
        HelpDeskDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var areaAccepts = await db.Areas.AnyAsync(a => a.Id == request.AreaId && a.IsActive && a.AcceptsRequests, ct);
        if (!areaAccepts)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["areaId"] = ["Elige un área válida."] });
        }

        var ticket = Ticket.Open(me.Id, request.AreaId, request.Description, request.Title, TicketPriority.Normal, clock.GetUtcNow());
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/tickets/{ticket.Id}", await ToDetailAsync(ticket, me, db, ct));
    }

    private static async Task<IResult> ListMine(CurrentUser me, HelpDeskDbContext db, CancellationToken ct)
    {
        var userId = me.Id;
        var items = await db.Tickets
            .Where(t => t.RequesterId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .Join(db.Areas, t => t.CurrentAreaId, a => a.Id, (t, a) => new TicketSummaryDto(
                t.Id, t.Title, t.Status, t.Priority, t.CurrentAreaId, a.Name, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(long id, CurrentUser me, HelpDeskDbContext db, CancellationToken ct)
    {
        var ticket = await LoadAsync(db, id, ct);
        if (ticket is null || !await CanSeeAsync(ticket, me, ct))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ToDetailAsync(ticket, me, db, ct));
    }

    private static async Task<IResult> AddMessage(
        long id,
        AddMessageRequest request,
        CurrentUser me,
        HelpDeskDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ticket = await LoadAsync(db, id, ct);
        if (ticket is null || !await CanSeeAsync(ticket, me, ct))
        {
            return TypedResults.NotFound();
        }

        // An agent writing on their own request speaks as the requester.
        var now = clock.GetUtcNow();
        if (ticket.RequesterId != me.Id && await ActingAreaAsync(ticket, me, ct) is { } areaId)
        {
            ticket.AddAgentMessage(me.Id, areaId, request.Body, request.Visibility, now);
        }
        else
        {
            ticket.AddRequesterMessage(request.Body, MessageSource.Portal, now);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDetailAsync(ticket, me, db, ct));
    }

    private static async Task<IResult> Reopen(long id, CurrentUser me, HelpDeskDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var ticket = await LoadAsync(db, id, ct);
        if (ticket is null || ticket.RequesterId != me.Id)
        {
            return TypedResults.NotFound();
        }

        ticket.Reopen(me.Id, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDetailAsync(ticket, me, db, ct));
    }

    /// <summary>Open segments for the caller's areas (or one area), oldest first: what each area has to work on.</summary>
    private static async Task<IResult> Queue(CurrentUser me, HelpDeskDbContext db, int? areaId = null, CancellationToken ct = default)
    {
        var areaIds = await me.AreaIdsAsync(ct);
        if (areaId is { } requested)
        {
            if (!areaIds.Contains(requested))
            {
                return TypedResults.Forbid();
            }

            areaIds = [requested];
        }

        var items = await db.TicketSegments
            .Where(s => s.Status == SegmentStatus.Open && areaIds.Contains(s.AreaId))
            // A segment that escalated and is waiting for another area is not this area's turn.
            .Where(s => !db.TicketSegments.Any(child => child.ParentSegmentId == s.Id && child.Status == SegmentStatus.Open))
            .OrderBy(s => s.OpenedAt)
            .Select(s => new QueueItemDto(
                s.TicketId, s.Ticket.Title, s.Ticket.Status, s.Ticket.Priority, s.Id, s.AreaId, s.AssigneeId,
                s.EscalationReason, s.OpenedAt, s.Ticket.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Escalate(
        long id,
        EscalateRequest request,
        CurrentUser me,
        HelpDeskDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ticket = await LoadAsync(db, id, ct);
        var segment = ticket?.Segments.SingleOrDefault(s => s.Id == request.SegmentId);
        if (ticket is null || segment is null || !await me.WorksInAsync(segment.AreaId, ct))
        {
            return TypedResults.NotFound();
        }

        if (!await db.Areas.AnyAsync(a => a.Id == request.ToAreaId && a.IsActive, ct))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["toAreaId"] = ["Elige un área válida."] });
        }

        ticket.Escalate(request.SegmentId, request.ToAreaId, me.Id, request.Reason, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDetailAsync(ticket, me, db, ct));
    }

    private static async Task<IResult> ResolveSegment(
        long id,
        Guid segmentId,
        ResolveRequest request,
        CurrentUser me,
        HelpDeskDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ticket = await LoadAsync(db, id, ct);
        var segment = ticket?.Segments.SingleOrDefault(s => s.Id == segmentId);
        if (ticket is null || segment is null || !await me.WorksInAsync(segment.AreaId, ct))
        {
            return TypedResults.NotFound();
        }

        ticket.ResolveSegment(segmentId, me.Id, request.Note, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDetailAsync(ticket, me, db, ct));
    }

    private static async Task<IResult> ResolveAll(
        long id,
        ResolveRequest request,
        CurrentUser me,
        HelpDeskDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ticket = await LoadAsync(db, id, ct);
        if (ticket is null || await ActingAreaAsync(ticket, me, ct) is null)
        {
            return TypedResults.NotFound();
        }

        ticket.ResolveAll(me.Id, request.Note, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDetailAsync(ticket, me, db, ct));
    }

    private static Task<Ticket?> LoadAsync(HelpDeskDbContext db, long id, CancellationToken ct) =>
        db.Tickets
            .Include(t => t.Segments)
            .Include(t => t.Messages)
            .AsSplitQuery()
            .SingleOrDefaultAsync(t => t.Id == id, ct);

    private static async Task<bool> CanSeeAsync(Ticket ticket, CurrentUser me, CancellationToken ct) =>
        ticket.RequesterId == me.Id || await ActingAreaAsync(ticket, me, ct) is not null;

    /// <summary>
    /// The caller's area on this ticket, if they work in one of the areas involved.
    /// Prefers the area whose turn it is, then any area with an open segment, then any involved area.
    /// </summary>
    private static async Task<int?> ActingAreaAsync(Ticket ticket, CurrentUser me, CancellationToken ct)
    {
        var mine = await me.AreaIdsAsync(ct);
        if (mine.Contains(ticket.CurrentAreaId))
        {
            return ticket.CurrentAreaId;
        }

        return ticket.Segments
            .Where(s => mine.Contains(s.AreaId))
            .OrderBy(s => s.Status == SegmentStatus.Open ? 0 : 1)
            .ThenByDescending(s => s.OpenedAt)
            .Select(s => (int?)s.AreaId)
            .FirstOrDefault();
    }

    private static async Task<TicketDetailDto> ToDetailAsync(Ticket ticket, CurrentUser me, HelpDeskDbContext db, CancellationToken ct)
    {
        var isAgent = await ActingAreaAsync(ticket, me, ct) is not null;
        var viewerAreas = isAgent ? await me.AreaIdsAsync(ct) : [];

        var messages = ticket.Messages.Where(m => m.IsVisibleTo(viewerAreas)).OrderBy(m => m.CreatedAt).ToList();
        var peopleIds = messages.Select(m => m.AuthorId)
            .Append(ticket.RequesterId)
            .Concat(ticket.Segments.Select(s => s.AssigneeId).OfType<Guid>())
            .Concat(ticket.Segments.Select(s => s.EscalatedById).OfType<Guid>())
            .Distinct()
            .ToList();
        var people = await db.Users
            .Where(u => peopleIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, string.IsNullOrEmpty(u.DisplayName) ? u.Email ?? "" : u.DisplayName), ct);

        var areaIds = ticket.Segments.Select(s => s.AreaId).Append(ticket.CurrentAreaId).Distinct().ToList();
        var areaNames = await db.Areas.Where(a => areaIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        PersonDto Person(Guid id) => people.GetValueOrDefault(id) ?? new PersonDto(id, "");
        PersonDto? MaybePerson(Guid? id) => id is { } value ? Person(value) : null;

        var segments = isAgent
            ? ticket.Segments.OrderBy(s => s.OpenedAt).Select(s => new SegmentDto(
                s.Id, s.AreaId, areaNames.GetValueOrDefault(s.AreaId, ""), s.ParentSegmentId, s.Status, s.EscalationReason,
                MaybePerson(s.EscalatedById), MaybePerson(s.AssigneeId), s.OpenedAt, s.ResolvedAt, s.ResolutionNote)).ToList()
            : null;

        return new TicketDetailDto(
            ticket.Id,
            ticket.Title,
            ticket.Description,
            ticket.Status,
            ticket.Priority,
            Person(ticket.RequesterId),
            ticket.CurrentAreaId,
            areaNames.GetValueOrDefault(ticket.CurrentAreaId, ""),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.ResolvedAt,
            segments,
            messages.Select(m => new MessageDto(m.Id, Person(m.AuthorId), m.AuthorAreaId, m.Body, m.Visibility, m.Source, m.CreatedAt)).ToList());
    }
}
