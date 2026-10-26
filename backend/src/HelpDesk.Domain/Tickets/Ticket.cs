namespace HelpDesk.Domain.Tickets;

/// <summary>
/// One request from one person. It never gets duplicated: when another area must help,
/// the ticket is escalated and a new <see cref="TicketSegment"/> is opened for that area.
/// Every change is recorded as a <see cref="TicketEvent"/>, which feeds the audit log and the dashboard.
/// </summary>
public class Ticket
{
    public const int MaxTitleLength = 120;

    private readonly List<TicketSegment> _segments = [];
    private readonly List<TicketMessage> _messages = [];
    private readonly List<TicketEvent> _events = [];

    private Ticket()
    {
    }

    /// <summary>Database identity; shown to people as the ticket number ("folio").</summary>
    public long Id { get; private set; }

    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public TicketStatus Status { get; private set; }
    public TicketPriority Priority { get; private set; }
    public Guid RequesterId { get; private set; }
    public int? CategoryId { get; private set; }

    /// <summary>The area whose turn it is: the one working on the most recent open segment.</summary>
    public int CurrentAreaId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? FirstRespondedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyCollection<TicketSegment> Segments => _segments;
    public IReadOnlyCollection<TicketMessage> Messages => _messages;
    public IReadOnlyCollection<TicketEvent> Events => _events;

    public TicketSegment RootSegment => _segments.Single(s => s.ParentSegmentId is null);

    public static Ticket Open(
        Guid requesterId,
        int areaId,
        string description,
        string? title,
        TicketPriority priority,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("Cuéntanos qué está pasando para poder ayudarte.");
        }

        description = description.Trim();
        var ticket = new Ticket
        {
            Title = string.IsNullOrWhiteSpace(title) ? TitleFrom(description) : Truncate(title.Trim(), MaxTitleLength),
            Description = description,
            Status = TicketStatus.New,
            Priority = priority,
            RequesterId = requesterId,
            CurrentAreaId = areaId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        ticket._segments.Add(TicketSegment.Start(ticket, areaId, parent: null, escalatedById: null, reason: null, now));
        ticket.Record(TicketEventType.Created, requesterId, now, toValue: TicketStatus.New.ToString(), areaId: areaId);
        return ticket;
    }

    /// <summary>
    /// Hands the ticket to another area. The escalating segment stays open, waiting for the answer,
    /// so the escalating area keeps seeing the ticket and is notified when the new segment is resolved.
    /// </summary>
    public TicketSegment Escalate(Guid segmentId, int toAreaId, Guid actorId, string reason, DateTimeOffset now)
    {
        EnsureNotFinished();
        var from = OpenSegment(segmentId);

        if (from.AreaId == toAreaId)
        {
            throw new DomainException("El ticket ya está en esa área.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Explica por qué escalas el ticket para que la otra área tenga contexto.");
        }

        var segment = TicketSegment.Start(this, toAreaId, from, actorId, reason.Trim(), now);
        _segments.Add(segment);
        CurrentAreaId = toAreaId;
        MoveTo(TicketStatus.InProgress, actorId, now);
        Record(TicketEventType.Escalated, actorId, now, fromValue: from.AreaId.ToString(), toValue: toAreaId.ToString(), areaId: toAreaId, segmentId: segment.Id);
        return segment;
    }

    /// <summary>
    /// "Resolver mi parte": closes one area's segment and returns the ticket to the area that escalated it.
    /// Resolving the root segment resolves the whole ticket.
    /// </summary>
    public void ResolveSegment(Guid segmentId, Guid actorId, string? note, DateTimeOffset now)
    {
        EnsureNotFinished();
        var segment = OpenSegment(segmentId);

        if (segment.ParentSegmentId is null)
        {
            ResolveAll(actorId, note, now);
            return;
        }

        if (_segments.Any(s => s.ParentSegmentId == segment.Id && s.Status == SegmentStatus.Open))
        {
            throw new DomainException("Otra área sigue trabajando en lo que escalaste; espera su respuesta o resuelve todo el ticket.");
        }

        segment.Resolve(note, now);
        CurrentAreaId = LatestOpenSegment()?.AreaId ?? CurrentAreaId;
        Touch(now);
        Record(TicketEventType.SegmentResolved, actorId, now, areaId: segment.AreaId, segmentId: segment.Id);
    }

    /// <summary>"Resolver todo": closes every open segment and resolves the ticket for the whole chain.</summary>
    public void ResolveAll(Guid actorId, string? note, DateTimeOffset now)
    {
        EnsureNotFinished();

        foreach (var segment in _segments.Where(s => s.Status == SegmentStatus.Open).OrderByDescending(s => s.OpenedAt))
        {
            segment.Resolve(note, now);
            Record(TicketEventType.SegmentResolved, actorId, now, areaId: segment.AreaId, segmentId: segment.Id);
        }

        ResolvedAt = now;
        CurrentAreaId = RootSegment.AreaId;
        MoveTo(TicketStatus.Resolved, actorId, now);
    }

    /// <summary>The requester was not satisfied: the ticket goes back to the area that first received it.</summary>
    public void Reopen(Guid actorId, DateTimeOffset now)
    {
        if (Status != TicketStatus.Resolved)
        {
            throw new DomainException("Solo se puede reabrir una solicitud resuelta.");
        }

        RootSegment.Reopen();
        ResolvedAt = null;
        CurrentAreaId = RootSegment.AreaId;
        Record(TicketEventType.Reopened, actorId, now, areaId: CurrentAreaId);
        MoveTo(TicketStatus.InProgress, actorId, now);
    }

    public void Close(Guid? actorId, DateTimeOffset now)
    {
        if (Status != TicketStatus.Resolved)
        {
            throw new DomainException("Solo se puede cerrar una solicitud resuelta.");
        }

        ClosedAt = now;
        MoveTo(TicketStatus.Closed, actorId, now);
    }

    public void WaitForRequester(Guid actorId, DateTimeOffset now)
    {
        EnsureNotFinished();
        MoveTo(TicketStatus.WaitingOnRequester, actorId, now);
    }

    public void AssignSegment(Guid segmentId, Guid? agentId, Guid actorId, DateTimeOffset now)
    {
        EnsureNotFinished();
        var segment = OpenSegment(segmentId);
        var previous = segment.AssigneeId;
        segment.AssigneeId = agentId;
        Touch(now);
        Record(TicketEventType.Assigned, actorId, now, previous?.ToString(), agentId?.ToString(), segment.AreaId, segment.Id);
    }

    public void ChangePriority(TicketPriority priority, Guid actorId, DateTimeOffset now)
    {
        if (priority == Priority)
        {
            return;
        }

        var previous = Priority;
        Priority = priority;
        Touch(now);
        Record(TicketEventType.PriorityChanged, actorId, now, previous.ToString(), priority.ToString());
    }

    public void ChangeCategory(int? categoryId, Guid actorId, DateTimeOffset now)
    {
        if (categoryId == CategoryId)
        {
            return;
        }

        var previous = CategoryId;
        CategoryId = categoryId;
        Touch(now);
        Record(TicketEventType.CategoryChanged, actorId, now, previous?.ToString(), categoryId?.ToString());
    }

    /// <summary>A message from the requester. Answering while we wait, or after a resolution, puts the ticket back in progress.</summary>
    public TicketMessage AddRequesterMessage(string body, MessageSource source, DateTimeOffset now)
    {
        if (Status == TicketStatus.Closed)
        {
            throw new DomainException("Esta solicitud ya está cerrada; levanta una nueva si necesitas más ayuda.");
        }

        var message = TicketMessage.Create(this, RequesterId, authorAreaId: null, body, MessageVisibility.Public, source, now);
        _messages.Add(message);
        Record(TicketEventType.Commented, RequesterId, now);

        if (Status == TicketStatus.Resolved)
        {
            Reopen(RequesterId, now);
        }
        else if (Status == TicketStatus.WaitingOnRequester)
        {
            MoveTo(TicketStatus.InProgress, RequesterId, now);
        }
        else
        {
            Touch(now);
        }

        return message;
    }

    /// <summary>A reply or internal note from an agent. The first public reply stops the first-response clock.</summary>
    public TicketMessage AddAgentMessage(Guid agentId, int agentAreaId, string body, MessageVisibility visibility, DateTimeOffset now)
    {
        if (Status == TicketStatus.Closed)
        {
            throw new DomainException("Esta solicitud ya está cerrada.");
        }

        var message = TicketMessage.Create(this, agentId, agentAreaId, body, visibility, MessageSource.Portal, now);
        _messages.Add(message);
        Record(TicketEventType.Commented, agentId, now, areaId: agentAreaId);

        if (visibility == MessageVisibility.Public)
        {
            FirstRespondedAt ??= now;
            if (Status == TicketStatus.New)
            {
                MoveTo(TicketStatus.InProgress, agentId, now);
                return message;
            }
        }

        Touch(now);
        return message;
    }

    public TicketSegment? LatestOpenSegment() =>
        _segments.Where(s => s.Status == SegmentStatus.Open).MaxBy(s => s.OpenedAt);

    private TicketSegment OpenSegment(Guid segmentId)
    {
        var segment = _segments.SingleOrDefault(s => s.Id == segmentId)
            ?? throw new DomainException("Ese tramo no pertenece a este ticket.");

        if (segment.Status != SegmentStatus.Open)
        {
            throw new DomainException("Ese tramo ya está resuelto.");
        }

        return segment;
    }

    private void EnsureNotFinished()
    {
        if (Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            throw new DomainException("La solicitud ya está resuelta; reábrela para seguir trabajando en ella.");
        }
    }

    private void MoveTo(TicketStatus status, Guid? actorId, DateTimeOffset now)
    {
        Touch(now);
        if (status == Status)
        {
            return;
        }

        var previous = Status;
        Status = status;
        Record(TicketEventType.StatusChanged, actorId, now, previous.ToString(), status.ToString());
    }

    private void Touch(DateTimeOffset now) => UpdatedAt = now;

    private void Record(
        TicketEventType type,
        Guid? actorId,
        DateTimeOffset now,
        string? fromValue = null,
        string? toValue = null,
        int? areaId = null,
        Guid? segmentId = null) =>
        _events.Add(new TicketEvent(this, type, actorId, now, fromValue, toValue, areaId, segmentId));

    private static string TitleFrom(string description)
    {
        var firstLine = description.Split('\n', 2)[0].Trim();
        return Truncate(firstLine, MaxTitleLength);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";
}
