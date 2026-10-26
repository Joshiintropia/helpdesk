using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HelpDesk.Infrastructure.Email;

/// <summary>Reads the help mailbox from any IMAP server (Gmail, Exchange on-premises, hosting providers...).</summary>
public sealed class ImapMailboxReader(IOptions<EmailOptions> options) : IMailboxReader
{
    private ImapOptions Imap => options.Value.Inbound.Imap;

    public async Task<IReadOnlyList<InboundEmail>> FetchUnreadAsync(CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var emails = new List<InboundEmail>();
        foreach (var uid in (await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken)).Take(25))
        {
            var message = await inbox.GetMessageAsync(uid, cancellationToken);
            var from = message.From.Mailboxes.FirstOrDefault();
            if (from is null)
            {
                continue;
            }

            emails.Add(new InboundEmail(
                uid.Id.ToString(),
                message.MessageId ?? $"imap-{inbox.UidValidity}-{uid.Id}",
                from.Address,
                from.Name,
                message.Subject ?? "",
                message.TextBody ?? HtmlToText(message.HtmlBody),
                message.Date,
                IsAutoReply(message)));
        }

        await client.DisconnectAsync(true, cancellationToken);
        return emails;
    }

    public async Task MarkProcessedAsync(InboundEmail email, CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(cancellationToken);
        await client.Inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
        await client.Inbox.AddFlagsAsync(new UniqueId(uint.Parse(email.ProviderId)), MessageFlags.Seen, silent: true, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private async Task<ImapClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new ImapClient();
        await client.ConnectAsync(Imap.Host, Imap.Port, Imap.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
        await client.AuthenticateAsync(Imap.Username, Imap.Password, cancellationToken);
        return client;
    }

    private static bool IsAutoReply(MimeMessage message)
    {
        var autoSubmitted = message.Headers[HeaderId.AutoSubmitted];
        return (!string.IsNullOrEmpty(autoSubmitted) && !autoSubmitted.Equals("no", StringComparison.OrdinalIgnoreCase))
            || message.Headers.Contains("X-Autoreply")
            || message.Headers.Contains("X-Autorespond");
    }

    internal static string HtmlToText(string? html) =>
        string.IsNullOrEmpty(html)
            ? ""
            : System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(html, @"<(br|/p|/div)\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                "<[^>]+>",
                ""));
}

/// <summary>Sends notifications through any SMTP server.</summary>
public sealed class SmtpMailSender(IOptions<EmailOptions> options) : IMailSender
{
    public async Task SendAsync(OutboundEmail email, CancellationToken cancellationToken)
    {
        var outbound = options.Value.Outbound;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(outbound.FromName, outbound.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName ?? email.To, email.To));
        message.Subject = email.Subject;
        message.Headers.Add(HeaderId.AutoSubmitted, "auto-generated");
        message.Body = new TextPart("plain") { Text = email.Body };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            outbound.Smtp.Host,
            outbound.Smtp.Port,
            outbound.Smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect,
            cancellationToken);
        if (!string.IsNullOrEmpty(outbound.Smtp.Username))
        {
            await client.AuthenticateAsync(outbound.Smtp.Username, outbound.Smtp.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
