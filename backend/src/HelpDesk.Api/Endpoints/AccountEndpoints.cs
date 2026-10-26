using HelpDesk.Api.Auth;
using HelpDesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HelpDesk.Api.Endpoints;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record AuthProvidersDto(bool Microsoft);

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/providers", (IOptions<MicrosoftLoginOptions> microsoft) =>
            TypedResults.Ok(new AuthProvidersDto(microsoft.Value.Enabled)));

        app.MapGet("/api/auth/external/microsoft", ChallengeMicrosoft);
        app.MapGet("/api/auth/external/callback", ExternalCallback);

        app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireAuthorization();

        app.MapPost("/api/account/change-password", ChangePassword).RequireAuthorization();
        return app;
    }

    private static IResult ChallengeMicrosoft(
        SignInManager<ApplicationUser> signIn,
        IOptions<MicrosoftLoginOptions> microsoft,
        string? returnUrl = null)
    {
        if (!microsoft.Value.Enabled)
        {
            return TypedResults.NotFound();
        }

        var callback = $"/api/auth/external/callback?returnUrl={Uri.EscapeDataString(LocalUrl(returnUrl))}";
        var properties = signIn.ConfigureExternalAuthenticationProperties(MicrosoftLoginOptions.Scheme, callback);
        return TypedResults.Challenge(properties, [MicrosoftLoginOptions.Scheme]);
    }

    /// <summary>Microsoft sends the browser back here; we sign the person in and return them to the app.</summary>
    private static async Task<IResult> ExternalCallback(
        HttpContext context,
        SignInManager<ApplicationUser> signIn,
        ExternalLoginService externalLogins,
        IOptions<MicrosoftLoginOptions> microsoft,
        string? returnUrl = null)
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return TypedResults.Redirect("/entrar?error=externo");
        }

        var result = await externalLogins.SignInAsync(info, microsoft.Value.AutoProvision);
        await context.SignOutAsync(IdentityConstants.ExternalScheme);

        return result switch
        {
            ExternalSignInResult.SignedIn => TypedResults.Redirect(LocalUrl(returnUrl)),
            ExternalSignInResult.Inactive => TypedResults.Redirect("/entrar?error=inactivo"),
            ExternalSignInResult.NotRegistered => TypedResults.Redirect("/entrar?error=no-registrado"),
            _ => TypedResults.Redirect("/entrar?error=externo"),
        };
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        CurrentUser me,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn)
    {
        var user = await users.FindByIdAsync(me.Id.ToString());
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return TypedResults.UnprocessableEntity(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }

    /// <summary>Only paths inside this app, so the login flow cannot be used to redirect people elsewhere.</summary>
    private static string LocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : "/";
}
