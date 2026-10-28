using HelpDesk.Domain.Sla;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Attachments;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Maintenance;

public sealed class TicketOptions
{
    public const string Section = "Tickets";

    /// <summary>Business days a resolved ticket waits for the requester before it closes on its own. 0 turns it off.</summary>
    public int AutoCloseBusinessDays { get; set; } = 3;

    /// <summary>Background housekeeping. Tests turn it off and call <see cref="TicketMaintenance"/> directly.</summary>
    public bool RunWorkers { get; set; } = true;
}

/// <summary>Housekeeping: closes resolved tickets nobody answered, and deletes uploads never sent.</summary>
public sealed class TicketMaintenance(
    HelpDeskDbContext db,
    AttachmentService attachments,
    IOptions<TicketOptions> options,
    TimeProvider clock,
    ILogger<TicketMaintenance> logger)
{
    public async Task<int> CloseResolvedAsync(CancellationToken cancellationToken)
    {
        var days = options.Value.AutoCloseBusinessDays;
        if (days <= 0)
        {
            return 0;
        }

        var calendar = await db.BusinessCalendars.AsNoTracking().Include(c => c.Holidays).OrderBy(c => c.Id).FirstAsync(cancellationToken);
        var time = new BusinessTime(calendar);
        var workday = (int)(calendar.WorkdayEnd - calendar.WorkdayStart).TotalMinutes;
        var now = clock.GetUtcNow();

        // Calendar days are a cheap lower bound; the business-time check decides.
        var candidates = await db.Tickets
            .Where(t => t.Status == TicketStatus.Resolved && t.ResolvedAt < now.AddDays(-days))
            .ToListAsync(cancellationToken);

        var closed = 0;
        foreach (var ticket in candidates.Where(t => time.Add(t.ResolvedAt!.Value, days * workday) <= now))
        {
            ticket.Close(actorId: null, now);
            closed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (closed > 0)
        {
            logger.LogInformation("Se cerraron {Count} solicitudes resueltas sin respuesta", closed);
        }

        return closed;
    }

    public Task<int> CleanupUploadsAsync(CancellationToken cancellationToken) =>
        attachments.CleanupAsync(TimeSpan.FromDays(1), cancellationToken);
}

/// <summary>Runs the housekeeping every hour.</summary>
public sealed class TicketMaintenanceWorker(IServiceScopeFactory scopes, ILogger<TicketMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<TicketMaintenance>();
                await maintenance.CloseResolvedAsync(stoppingToken);
                await maintenance.CleanupUploadsAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Error en el mantenimiento de solicitudes");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
