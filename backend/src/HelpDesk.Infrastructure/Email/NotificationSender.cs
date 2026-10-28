using System.Text;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Email;

/// <summary>Sends pending outbox rows. Failures are retried a few times and then left for an administrator to see.</summary>
public class NotificationSender(
    HelpDeskDbContext db,
    IMailSender sender,
    IOptions<EmailOptions> options,
    TimeProvider clock,
    ILogger<NotificationSender> logger)
{
    public const int MaxAttempts = 5;

    public async Task<int> SendPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await db.EmailNotifications
            .Include(n => n.Ticket)
            .Where(n => n.SentAt == null && n.Attempts < MaxAttempts)
            .OrderBy(n => n.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var notification in pending)
        {
            try
            {
                if (await RenderAsync(notification, cancellationToken) is { } email)
                {
                    await sender.SendAsync(email, cancellationToken);
                    sent++;
                }

                notification.SentAt = clock.GetUtcNow();
                notification.LastError = null;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                notification.Attempts++;
                notification.LastError = error.Message.Length > 1000 ? error.Message[..1000] : error.Message;
                logger.LogWarning(error, "No se pudo enviar la notificación {Id} del ticket {TicketId}", notification.Id, notification.TicketId);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    /// <summary>Builds the email, or null when there is nobody to send it to.</summary>
    private async Task<OutboundEmail?> RenderAsync(EmailNotification notification, CancellationToken cancellationToken)
    {
        var ticket = notification.Ticket;
        var requester = await db.Users.Where(u => u.Id == ticket.RequesterId)
            .Select(u => new { u.Email, u.DisplayName, u.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (requester is null || !requester.IsActive || string.IsNullOrWhiteSpace(requester.Email))
        {
            return null;
        }

        var link = $"{options.Value.AppUrl.TrimEnd('/')}/solicitudes/{ticket.Id}";
        var tag = EmailText.SubjectTag(ticket.Id);
        var greeting = string.IsNullOrWhiteSpace(requester.DisplayName) || requester.DisplayName == requester.Email
            ? "Hola,"
            : $"Hola {requester.DisplayName},";
        var body = new StringBuilder().AppendLine(greeting).AppendLine();
        string subject;

        switch (notification.Kind)
        {
            case NotificationKind.Received:
                subject = $"{tag} Recibimos tu solicitud: {ticket.Title}";
                body.AppendLine($"Recibimos tu solicitud con el folio {ticket.Id}. Te avisaremos por este medio cuando tengamos novedades.")
                    .AppendLine()
                    .AppendLine("Si quieres agregar algo, responde a este correo sin cambiar el asunto.");
                break;

            case NotificationKind.AgentReply:
                var message = await db.TicketMessages.Where(m => m.Id == notification.MessageId)
                    .Select(m => new { m.Body, m.AuthorId })
                    .SingleOrDefaultAsync(cancellationToken);
                if (message is null)
                {
                    return null;
                }

                var author = await db.Users.Where(u => u.Id == message.AuthorId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken);
                subject = $"{tag} Respuesta a tu solicitud: {ticket.Title}";
                body.AppendLine($"{(string.IsNullOrWhiteSpace(author) ? "Nuestro equipo" : author)} respondió a tu solicitud:")
                    .AppendLine()
                    .AppendLine(message.Body)
                    .AppendLine()
                    .AppendLine("Puedes contestar respondiendo a este correo.");
                break;

            case NotificationKind.Resolved:
                subject = $"{tag} Tu solicitud quedó resuelta: {ticket.Title}";
                body.AppendLine("Marcamos tu solicitud como resuelta.")
                    .AppendLine()
                    .AppendLine("Si el problema sigue, responde a este correo y la reabrimos.");
                break;

            default:
                return null;
        }

        body.AppendLine().AppendLine($"Ver tu solicitud: {link}");
        return new OutboundEmail(requester.Email, requester.DisplayName, subject, body.ToString());
    }
}
