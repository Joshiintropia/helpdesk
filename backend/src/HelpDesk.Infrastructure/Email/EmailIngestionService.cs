using HelpDesk.Domain;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Email;

/// <summary>
/// Turns one email from the help mailbox into a new ticket, or into a message on an existing one when
/// the subject carries its "[#123]" tag and the sender takes part in that ticket.
/// </summary>
public class EmailIngestionService(
    HelpDeskDbContext db,
    UserManager<ApplicationUser> users,
    IOptions<EmailOptions> options,
    TimeProvider clock,
    ILogger<EmailIngestionService> logger)
{
    public async Task<InboundOutcome?> ProcessAsync(InboundEmail email, CancellationToken cancellationToken)
    {
        if (await db.ProcessedEmails.AnyAsync(p => p.MessageId == email.MessageId, cancellationToken))
        {
            return null;
        }

        var (outcome, ticketId) = await HandleAsync(email, cancellationToken);
        db.ProcessedEmails.Add(new ProcessedEmail
        {
            MessageId = email.MessageId,
            FromAddress = email.FromAddress,
            Subject = Truncate(email.Subject, 500),
            Outcome = outcome,
            TicketId = ticketId,
            ProcessedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Correo {MessageId} de {From}: {Outcome} (ticket {TicketId})", email.MessageId, email.FromAddress, outcome, ticketId);
        return outcome;
    }

    private async Task<(InboundOutcome Outcome, long? TicketId)> HandleAsync(InboundEmail email, CancellationToken cancellationToken)
    {
        if (email.IsAutoReply)
        {
            return (InboundOutcome.IgnoredAutoReply, null);
        }

        var ownAddresses = new[] { options.Value.Graph.Mailbox, options.Value.Outbound.FromAddress, options.Value.Inbound.Imap.Username };
        if (ownAddresses.Any(a => !string.IsNullOrWhiteSpace(a) && a.Equals(email.FromAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return (InboundOutcome.IgnoredOwnMailbox, null);
        }

        var sender = await FindOrCreateSenderAsync(email);
        if (sender is null)
        {
            return (InboundOutcome.IgnoredUnknownSender, null);
        }

        if (!sender.IsActive)
        {
            return (InboundOutcome.IgnoredInactiveSender, null);
        }

        var now = clock.GetUtcNow();
        if (EmailText.TicketIdFromSubject(email.Subject) is { } ticketId
            && await TryAddToTicketAsync(ticketId, sender, email, now, cancellationToken))
        {
            return (InboundOutcome.MessageAdded, ticketId);
        }

        var body = email.Body.Trim();
        var subject = EmailText.CleanSubject(email.Subject);
        if (body.Length == 0 && subject.Length == 0)
        {
            return (InboundOutcome.IgnoredEmpty, null);
        }

        var ticket = Ticket.Open(
            sender.Id,
            await DefaultAreaIdAsync(cancellationToken),
            body.Length > 0 ? body : subject,
            subject.Length > 0 ? subject : null,
            TicketPriority.Normal,
            now);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
        return (InboundOutcome.TicketCreated, ticket.Id);
    }

    /// <summary>Adds the reply to the ticket when the sender is its requester or works in one of its areas.</summary>
    private async Task<bool> TryAddToTicketAsync(long ticketId, ApplicationUser sender, InboundEmail email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.Include(t => t.Segments).SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken);
        if (ticket is null || ticket.Status == TicketStatus.Closed)
        {
            return false;
        }

        var reply = EmailText.StripQuotedReply(email.Body);
        if (reply.Length == 0)
        {
            return false;
        }

        try
        {
            if (ticket.RequesterId == sender.Id)
            {
                ticket.AddRequesterMessage(reply, MessageSource.Email, now);
            }
            else
            {
                var senderAreas = await db.AreaMembers.Where(m => m.UserId == sender.Id).Select(m => m.AreaId).ToListAsync(cancellationToken);
                var area = ticket.Segments
                    .Where(s => senderAreas.Contains(s.AreaId))
                    .OrderBy(s => s.Status == SegmentStatus.Open ? 0 : 1)
                    .ThenByDescending(s => s.OpenedAt)
                    .Select(s => (int?)s.AreaId)
                    .FirstOrDefault();
                if (area is null)
                {
                    return false;
                }

                ticket.AddAgentMessage(sender.Id, area.Value, reply, MessageVisibility.Public, now);
            }
        }
        catch (DomainException)
        {
            return false;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ApplicationUser?> FindOrCreateSenderAsync(InboundEmail email)
    {
        var user = await users.FindByEmailAsync(email.FromAddress);
        if (user is not null || !options.Value.Inbound.CreateUnknownSenders)
        {
            return user;
        }

        // No password: they can follow the ticket by email, or an administrator can give them access later.
        user = new ApplicationUser
        {
            UserName = email.FromAddress,
            Email = email.FromAddress,
            DisplayName = string.IsNullOrWhiteSpace(email.FromName) ? email.FromAddress : email.FromName.Trim(),
        };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            logger.LogWarning("No se pudo crear el usuario {Email}: {Errors}", email.FromAddress, string.Join("; ", created.Errors.Select(e => e.Description)));
            return null;
        }

        return user;
    }

    private async Task<int> DefaultAreaIdAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Inbound.DefaultAreaId is { } configured && await db.Areas.AnyAsync(a => a.Id == configured && a.IsActive, cancellationToken))
        {
            return configured;
        }

        return await db.Areas
            .Where(a => a.IsActive && a.AcceptsRequests)
            .OrderBy(a => a.Id)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No hay ninguna área activa para recibir tickets por correo.");
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
