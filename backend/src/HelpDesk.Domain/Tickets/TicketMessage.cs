namespace HelpDesk.Domain.Tickets;

/// <summary>One entry in the ticket's conversation, from the requester or an agent.</summary>
public class TicketMessage
{
    private TicketMessage()
    {
    }

    public Guid Id { get; private set; }
    public long TicketId { get; private set; }
    public Ticket Ticket { get; private set; } = null!;
    public Guid AuthorId { get; private set; }

    /// <summary>The agent's area when the author is an agent; null for the requester.</summary>
    public int? AuthorAreaId { get; private set; }

    public string Body { get; private set; } = "";
    public MessageVisibility Visibility { get; private set; }
    public MessageSource Source { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    internal static TicketMessage Create(
        Ticket ticket,
        Guid authorId,
        int? authorAreaId,
        string body,
        MessageVisibility visibility,
        MessageSource source,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new DomainException("El mensaje está vacío.");
        }

        return new TicketMessage
        {
            Id = Guid.CreateVersion7(now),
            Ticket = ticket,
            AuthorId = authorId,
            AuthorAreaId = authorAreaId,
            Body = body.Trim(),
            Visibility = visibility,
            Source = source,
            CreatedAt = now,
        };
    }

    /// <summary>Whether someone in <paramref name="viewerAreaIds"/> (empty for a requester) may read this message.</summary>
    public bool IsVisibleTo(IReadOnlyCollection<int> viewerAreaIds) => Visibility switch
    {
        MessageVisibility.Public => true,
        MessageVisibility.InternalAllAreas => viewerAreaIds.Count > 0,
        MessageVisibility.InternalOwnArea => AuthorAreaId is { } area && viewerAreaIds.Contains(area),
        _ => false,
    };
}
