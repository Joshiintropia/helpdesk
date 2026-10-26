using HelpDesk.Infrastructure.Identity;

namespace HelpDesk.Api.Auth;

/// <summary>
/// While someone still has an administrator's temporary password, the API only lets them see who they are,
/// change the password or sign out.
/// </summary>
public static class MustChangePasswordMiddleware
{
    private static readonly string[] AllowedPrefixes = ["/api/me", "/api/account/change-password", "/api/auth/"];

    public static IApplicationBuilder UseMustChangePassword(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (context.User.HasClaim(AppClaims.MustChangePassword, "true")
                && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                && !AllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Antes de continuar, cambia tu contraseña temporal.", code = "must_change_password" });
                return;
            }

            await next();
        });
}
