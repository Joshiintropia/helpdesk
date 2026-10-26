using HelpDesk.Infrastructure.Email;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpDesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("HelpDesk")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:HelpDesk'.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<NotificationInterceptor>();
        services.AddDbContext<HelpDeskDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<NotificationInterceptor>()));

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
        services.AddEmail(configuration);
        return services;
    }

    private static void AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var email = configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();
        if (email.Inbound.Provider is MailProvider.Smtp || email.Outbound.Provider is MailProvider.Imap)
        {
            throw new InvalidOperationException("Email:Inbound:Provider acepta None, Graph o Imap; Email:Outbound:Provider acepta None, Graph o Smtp.");
        }

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.AddScoped<EmailIngestionService>();
        services.AddHttpClient<GraphMailClient>();

        switch (email.Inbound.Provider)
        {
            case MailProvider.Graph:
                services.AddScoped<IMailboxReader>(sp => sp.GetRequiredService<GraphMailClient>());
                break;
            case MailProvider.Imap:
                services.AddScoped<IMailboxReader, ImapMailboxReader>();
                break;
        }

        switch (email.Outbound.Provider)
        {
            case MailProvider.Graph:
                services.AddScoped<IMailSender>(sp => sp.GetRequiredService<GraphMailClient>());
                break;
            case MailProvider.Smtp:
                services.AddScoped<IMailSender, SmtpMailSender>();
                break;
        }

        if (email.Outbound.Provider != MailProvider.None)
        {
            services.AddScoped<NotificationSender>();
        }

        if (email.RunWorkers && email.Inbound.Provider != MailProvider.None)
        {
            services.AddHostedService<InboundMailWorker>();
        }

        if (email.RunWorkers && email.Outbound.Provider != MailProvider.None)
        {
            services.AddHostedService<OutboundMailWorker>();
        }
    }
}
