using System.Security.Claims;
using HelpDesk.Api.Auth;
using HelpDesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Api;

/// <summary>What happens when someone comes back from Microsoft 365, without calling Microsoft.</summary>
public class ExternalLoginTests(HelpDeskApiFactory factory) : IClassFixture<HelpDeskApiFactory>
{
    [Fact]
    public async Task Links_the_Microsoft_account_to_the_user_an_administrator_already_created()
    {
        var existing = await CreateUserAsync("ana@empresa.test", isActive: true);

        var result = await SignInAsync(Info("ms-ana", "ana@empresa.test"), autoProvision: false);

        Assert.Equal(ExternalSignInResult.SignedIn, result);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var linked = await users.FindByLoginAsync(MicrosoftLoginOptions.Scheme, "ms-ana");
        Assert.Equal(existing, linked!.Id);
    }

    [Fact]
    public async Task Creates_a_requester_account_on_first_sign_in_when_auto_provisioning()
    {
        var result = await SignInAsync(Info("ms-beto", "beto@empresa.test", "Beto Ruiz"), autoProvision: true);

        Assert.Equal(ExternalSignInResult.SignedIn, result);
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("beto@empresa.test");
        Assert.Equal("Beto Ruiz", user!.DisplayName);
        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public async Task Rejects_unknown_people_when_auto_provisioning_is_off()
    {
        Assert.Equal(ExternalSignInResult.NotRegistered, await SignInAsync(Info("ms-x", "nadie@empresa.test"), autoProvision: false));
    }

    [Fact]
    public async Task Rejects_deactivated_accounts()
    {
        await CreateUserAsync("baja@empresa.test", isActive: false);

        Assert.Equal(ExternalSignInResult.Inactive, await SignInAsync(Info("ms-baja", "baja@empresa.test"), autoProvision: true));
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    public void Multi_tenant_authorities_are_rejected(string tenant)
    {
        var options = new MicrosoftLoginOptions { Enabled = true, TenantId = tenant, ClientId = "id", ClientSecret = "secret" };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static ExternalLoginInfo Info(string key, string email, string? name = null)
    {
        var claims = new List<Claim> { new("preferred_username", email) };
        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return new ExternalLoginInfo(principal, MicrosoftLoginOptions.Scheme, key, "Microsoft 365");
    }

    private async Task<Guid> CreateUserAsync(string email, bool isActive)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email, IsActive = isActive };
        Assert.True((await users.CreateAsync(user, "Contrasena123")).Succeeded);
        return user.Id;
    }

    private async Task<ExternalSignInResult> SignInAsync(ExternalLoginInfo info, bool autoProvision)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return await scope.ServiceProvider.GetRequiredService<ExternalLoginService>().SignInAsync(info, autoProvision);
    }
}
