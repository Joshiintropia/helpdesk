using Microsoft.AspNetCore.DataProtection;

namespace HelpDesk.Infrastructure.Email;

/// <summary>
/// Signed links to rate a ticket without signing in: people who only write by email have no password.
/// A link only rates the ticket it names and expires after 30 days.
/// </summary>
public sealed class SurveyLinks(IDataProtectionProvider provider, TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("HelpDesk.Survey.v1").ToTimeLimitedDataProtector();

    public string TokenFor(long ticketId) => _protector.Protect(ticketId.ToString(), clock.GetUtcNow() + Lifetime);

    /// <summary>The ticket the token names, or null when it is invalid or expired.</summary>
    public long? TicketIdFrom(string token)
    {
        try
        {
            var value = _protector.Unprotect(token, out var expiration);
            return expiration >= clock.GetUtcNow() && long.TryParse(value, out var id) ? id : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
