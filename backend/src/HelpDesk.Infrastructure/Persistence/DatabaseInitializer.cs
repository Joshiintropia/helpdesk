using HelpDesk.Domain.Sla;
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
