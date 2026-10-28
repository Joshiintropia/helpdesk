using Microsoft.AspNetCore.Identity;

namespace HelpDesk.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";

    /// <summary>Inactive users cannot sign in, with a password or with Microsoft 365.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Set when an administrator creates or resets the password; cleared when the user picks their own.</summary>
    public bool MustChangePassword { get; set; }
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Supervisor = "Supervisor";
    public const string Agent = "Agent";

    public static readonly string[] All = [Admin, Supervisor, Agent];
}
