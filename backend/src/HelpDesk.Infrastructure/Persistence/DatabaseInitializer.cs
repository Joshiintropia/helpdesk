using HelpDesk.Domain.Sla;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and creates the data every installation needs: roles, a default business
/// calendar and the first administrator (taken from configuration, never hard-coded).
/// </summary>
public class DatabaseInitializer(
    HelpDeskDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        if (!await db.BusinessCalendars.AnyAsync(cancellationToken))
        {
            db.BusinessCalendars.Add(new BusinessCalendar { Name = "Horario laboral" });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.SlaPolicies.AnyAsync(cancellationToken))
        {
            // Starting targets in business minutes (a 9-hour day); administrators adjust them in "SLA".
            var calendarId = await db.BusinessCalendars.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync(cancellationToken);
            db.SlaPolicies.AddRange(
                new SlaPolicy { Name = "Urgente", Priority = TicketPriority.Urgent, FirstResponseMinutes = 30, ResolutionMinutes = 4 * 60, CalendarId = calendarId },
                new SlaPolicy { Name = "Alta", Priority = TicketPriority.High, FirstResponseMinutes = 2 * 60, ResolutionMinutes = 9 * 60, CalendarId = calendarId },
                new SlaPolicy { Name = "Normal", Priority = TicketPriority.Normal, FirstResponseMinutes = 4 * 60, ResolutionMinutes = 3 * 9 * 60, CalendarId = calendarId },
                new SlaPolicy { Name = "Baja", Priority = TicketPriority.Low, FirstResponseMinutes = 9 * 60, ResolutionMinutes = 5 * 9 * 60, CalendarId = calendarId });
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsureAdminAsync();
    }

    private async Task EnsureAdminAsync()
    {
        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Administrador" };
        var result = await users.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            logger.LogError("No se pudo crear el administrador inicial: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await users.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Administrador inicial creado: {Email}", email);
    }
}
