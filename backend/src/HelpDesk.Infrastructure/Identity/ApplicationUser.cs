using Microsoft.AspNetCore.Identity;

namespace HelpDesk.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Supervisor = "Supervisor";
    public const string Agent = "Agent";

    public static readonly string[] All = [Admin, Supervisor, Agent];
}
