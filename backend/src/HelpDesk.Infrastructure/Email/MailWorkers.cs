using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Email;

/// <summary>Reads the help mailbox every few seconds and turns new emails into tickets or messages.</summary>
public sealed class InboundMailWorker(IServiceScopeFactory scopes, IOptions<EmailOptions> options, ILogger<InboundMailWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, options.Value.Inbound.PollSeconds)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var reader = scope.ServiceProvider.GetRequiredService<IMailboxReader>();
                foreach (var email in await reader.FetchUnreadAsync(stoppingToken))
                {
                    // A fresh scope per email so one bad message cannot leave tracked changes for the next.
                    await using var emailScope = scopes.CreateAsyncScope();
                    await emailScope.ServiceProvider.GetRequiredService<EmailIngestionService>().ProcessAsync(email, stoppingToken);
                    await reader.MarkProcessedAsync(email, stoppingToken);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Error al leer el buzón de ayuda");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Sends the notification outbox every few seconds.</summary>
public sealed class OutboundMailWorker(IServiceScopeFactory scopes, ILogger<OutboundMailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationSender>().SendPendingAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Error al enviar notificaciones");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
