namespace HelpDesk.Infrastructure.Email;

/// <param name="ProviderId">The provider's own id, used to mark the message as processed.</param>
/// <param name="MessageId">The RFC 5322 Message-ID, used to never process the same email twice.</param>
/// <param name="IsAutoReply">Out-of-office and other automatic replies, which must not create tickets.</param>
public sealed record InboundEmail(
    string ProviderId,
    string MessageId,
    string FromAddress,
    string? FromName,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAt,
    bool IsAutoReply = false);

public sealed record OutboundEmail(string To, string? ToName, string Subject, string Body);

public interface IMailboxReader
{
    Task<IReadOnlyList<InboundEmail>> FetchUnreadAsync(CancellationToken cancellationToken);

    /// <summary>Marks the email as read so it is not fetched again.</summary>
    Task MarkProcessedAsync(InboundEmail email, CancellationToken cancellationToken);
}

public interface IMailSender
{
    Task SendAsync(OutboundEmail email, CancellationToken cancellationToken);
}
