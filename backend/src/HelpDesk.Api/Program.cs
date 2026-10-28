using System.Text.Json.Serialization;
using HelpDesk.Api.Auth;
using HelpDesk.Api.Endpoints;
using HelpDesk.Domain;
using HelpDesk.Infrastructure;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

var microsoftLogin = builder.Configuration.GetSection(MicrosoftLoginOptions.Section).Get<MicrosoftLoginOptions>() ?? new();
builder.Services.Configure<MicrosoftLoginOptions>(builder.Configuration.GetSection(MicrosoftLoginOptions.Section));

var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
authentication.AddIdentityCookies();
authentication.AddMicrosoftLogin(microsoftLogin);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "helpdesk.auth";
    // Lax (not Strict) so the session survives the redirect back from Microsoft 365.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(10);

    // This is an API: answer 401/403 instead of redirecting to a login page.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();

// Behind nginx (docker-compose) or another reverse proxy: trust X-Forwarded-Proto so cookies and the
// Microsoft redirect use https. Only the proxy is published, so any forwarder is accepted.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<HelpDeskDbContext>();

var app = builder.Build();

app.UseForwardedHeaders();

// Business-rule violations become 422 with a message the UI can show as-is.
app.UseExceptionHandler(errors => errors.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (error is DomainException domainError)
    {
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await context.Response.WriteAsJsonAsync(new { message = domainError.Message });
        return;
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { message = "Ocurrió un error inesperado." });
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

app.UseAuthentication();
app.UseMustChangePassword();
app.UseAuthorization();

app.MapHealthChecks("/health");

var allowSelfRegistration = app.Configuration.GetValue("Auth:AllowSelfRegistration", false);
app.MapGroup("/api/auth")
    .AddEndpointFilter(async (context, next) =>
        !allowSelfRegistration && context.HttpContext.Request.Path.Value?.EndsWith("/register", StringComparison.OrdinalIgnoreCase) == true
            ? TypedResults.NotFound()
            : await next(context))
    .MapIdentityApi<ApplicationUser>();
app.MapAccountEndpoints();
app.MapUserAdminEndpoints();
app.MapAreaEndpoints();
app.MapTicketEndpoints();
app.MapSlaEndpoints();

app.Run();

public partial class Program;
