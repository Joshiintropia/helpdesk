using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("HelpDesk")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:HelpDesk'.");

        services.AddDbContext<HelpDeskDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HelpDeskDbContext>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>()
            .AddDefaultTokenProviders()
            .AddApiEndpoints()
            // After AddApiEndpoints, which registers the default SignInManager and would replace ours.
            .AddSignInManager<AppSignInManager>();

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<ExternalLoginService>();
        return services;
    }
}
