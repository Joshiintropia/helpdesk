using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Identity;

public static class AppClaims
{
    /// <summary>Present while the user still has the temporary password an administrator gave them.</summary>
    public const string MustChangePassword = "must_change_password";
}

public class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(AppClaims.MustChangePassword, "true"));
        }

        return identity;
    }
}
