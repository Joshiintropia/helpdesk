using HelpDesk.Domain.Tickets;

namespace HelpDesk.Infrastructure.Email;

/// <summary>Every email read from the help mailbox and what was done with it; also prevents processing it twice.</summary>
public class ProcessedEmail
{
    public long Id { get; set; }
    public required string MessageId { get; set; }
    public required string FromAddress { get; set; }
    public required string Subject { get; set; }
    public InboundOutcome Outcome { get; set; }
    public long? TicketId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public enum InboundOutcome
{
    TicketCreated,
    MessageAdded,
    IgnoredAutoReply,
    IgnoredOwnMailbox,
    IgnoredUnknownSender,
    IgnoredInactiveSender,
    IgnoredEmpty,
}

/// <summary>
/// Outbox of emails to the requester. Rows are written in the same transaction as the ticket change that
/// causes them, and a background worker sends them, so a mail server outage never loses or blocks work.
/// </summary>
public class EmailNotification
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public NotificationKind Kind { get; set; }

    /// <summary>For agent replies: the message to include.</summary>
    public Guid? MessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public enum NotificationKind
{
    /// <summary>"Recibimos tu solicitud", with the ticket number.</summary>
    Received,

    /// <summary>An agent answered the requester.</summary>
    AgentReply,

    /// <summary>The ticket was resolved; the requester can reply to reopen it.</summary>
    Resolved,
}
