using HelpDesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace HelpDesk.Api.Auth;

/// <summary>
/// "Auth:Microsoft" settings. Sign-in with Microsoft 365 is optional: installations without it keep using
/// accounts an administrator creates. Both can be on at the same time.
/// </summary>
public sealed class MicrosoftLoginOptions
{
    public const string Section = "Auth:Microsoft";
    public const string Scheme = "Microsoft";
    public const string CallbackPath = "/api/auth/signin-microsoft";

    public bool Enabled { get; set; }

    /// <summary>The organization's tenant id (or domain). Multi-tenant values are rejected.</summary>
    public string TenantId { get; set; } = "";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// When true, anyone in the tenant can sign in and gets a requester account on first use.
    /// When false, only people an administrator already registered (by email) can sign in with Microsoft.
    /// </summary>
    public bool AutoProvision { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("Para entrar con Microsoft 365 configura Auth:Microsoft:TenantId, ClientId y ClientSecret.");
        }

        // Accounts are matched by email, which is only trustworthy inside one organization's tenant.
        if (TenantId.Trim().ToLowerInvariant() is "common" or "organizations" or "consumers")
        {
            throw new InvalidOperationException("Auth:Microsoft:TenantId debe ser el tenant de tu organización, no 'common', 'organizations' ni 'consumers'.");
        }
    }
}

public static class MicrosoftLoginExtensions
{
    public static AuthenticationBuilder AddMicrosoftLogin(this AuthenticationBuilder auth, MicrosoftLoginOptions microsoft)
    {
        if (!microsoft.Enabled)
        {
            return auth;
        }

        microsoft.Validate();
        return auth.AddOpenIdConnect(MicrosoftLoginOptions.Scheme, "Microsoft 365", options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.Authority = $"https://login.microsoftonline.com/{microsoft.TenantId}/v2.0";
            options.ClientId = microsoft.ClientId;
            options.ClientSecret = microsoft.ClientSecret;
            options.ResponseType = "code";
            options.CallbackPath = MicrosoftLoginOptions.CallbackPath;
            options.MapInboundClaims = false;
            options.Scope.Add("email");
            options.Scope.Add("profile");
        });
    }
}
