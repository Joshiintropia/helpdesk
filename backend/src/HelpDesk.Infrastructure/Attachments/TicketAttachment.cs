using HelpDesk.Domain.Tickets;

namespace HelpDesk.Infrastructure.Attachments;

/// <summary>
/// A file on a ticket. It is uploaded first (no ticket yet) and linked when the ticket or message that
/// carries it is saved; files never linked are cleaned up after a day. Attached to a message, it is
/// visible to whoever can read that message; attached to the ticket itself, to everyone on the ticket.
/// </summary>
public class TicketAttachment
{
    public Guid Id { get; set; }
    public long? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>The message it came with; null for files sent with the original request.</summary>
    public Guid? MessageId { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }

    /// <summary>Where the bytes live in the file store; never derived from the file name.</summary>
    public required string StorageKey { get; set; }

    public Guid UploadedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
