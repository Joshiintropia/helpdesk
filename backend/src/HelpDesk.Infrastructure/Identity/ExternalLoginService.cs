using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Identity;

public enum ExternalSignInResult
{
    SignedIn,

    /// <summary>The account exists but an administrator deactivated it.</summary>
    Inactive,

    /// <summary>No account with that email and automatic creation is turned off.</summary>
    NotRegistered,

    /// <summary>The provider did not send an email address.</summary>
    MissingEmail,
}

/// <summary>
/// Signs in someone coming back from Microsoft 365. Links the Microsoft account to an existing user with the
/// same email (so accounts an administrator created keep their roles and areas) or, when allowed, creates one.
/// </summary>
public class ExternalLoginService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ILogger<ExternalLoginService> logger)
{
    public async Task<ExternalSignInResult> SignInAsync(ExternalLoginInfo info, bool autoProvision)
    {
        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);

        if (user is null)
        {
            var email = EmailOf(info.Principal);
            if (string.IsNullOrWhiteSpace(email))
            {
                return ExternalSignInResult.MissingEmail;
            }

            user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                if (!autoProvision)
                {
                    return ExternalSignInResult.NotRegistered;
                }

                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = info.Principal.FindFirstValue("name") ?? info.Principal.FindFirstValue(ClaimTypes.Name) ?? email,
                };
                EnsureSucceeded(await users.CreateAsync(user), "crear el usuario");
                logger.LogInformation("Usuario creado al entrar con {Provider}: {Email}", info.LoginProvider, email);
            }

            if (!user.IsActive)
            {
                return ExternalSignInResult.Inactive;
            }

            EnsureSucceeded(await users.AddLoginAsync(user, info), "vincular la cuenta");
        }

        if (!user.IsActive)
        {
            return ExternalSignInResult.Inactive;
        }

        await signIn.SignInAsync(user, isPersistent: true, info.LoginProvider);
        return ExternalSignInResult.SignedIn;
    }

    private static string? EmailOf(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue("email")
        ?? principal.FindFirstValue("preferred_username");

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"No se pudo {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
