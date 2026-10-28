using HelpDesk.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Email;

/// <summary>
/// Turns ticket changes into outbox rows as they are saved, so every way of changing a ticket (web, API,
/// email) notifies the requester the same way without each caller remembering to.
/// </summary>
public sealed class NotificationInterceptor(IOptions<EmailOptions> options, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enqueue(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Enqueue(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Enqueue(DbContext? context)
    {
        if (context is null || options.Value.Outbound.Provider == MailProvider.None)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var notifications = new List<EmailNotification>();

        foreach (var entry in Added<TicketEvent>(context))
        {
            var ticketEvent = entry.Entity;
            var kind = ticketEvent.Type switch
            {
                TicketEventType.Created => NotificationKind.Received,
                TicketEventType.StatusChanged when ticketEvent.ToValue == nameof(TicketStatus.Resolved) => NotificationKind.Resolved,
                _ => (NotificationKind?)null,
            };

            if (kind is { } value)
            {
                notifications.Add(new EmailNotification { Ticket = ticketEvent.Ticket, Kind = value, CreatedAt = now });
            }
        }

        foreach (var entry in Added<TicketMessage>(context))
        {
            var message = entry.Entity;
            if (message.Visibility == MessageVisibility.Public && message.AuthorId != message.Ticket.RequesterId)
            {
                notifications.Add(new EmailNotification
                {
                    Ticket = message.Ticket,
                    Kind = NotificationKind.AgentReply,
                    MessageId = message.Id,
                    CreatedAt = now,
                });
            }
        }

        context.AddRange(notifications);
    }

    private static IEnumerable<EntityEntry<T>> Added<T>(DbContext context)
        where T : class =>
        context.ChangeTracker.Entries<T>().Where(e => e.State == EntityState.Added).ToList();
}
