using HelpDesk.Api.Auth;
using HelpDesk.Domain.Areas;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Endpoints;

public record UserAdminDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    bool MustChangePassword,
    bool MicrosoftLinked,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<int> AreaIds);

/// <param name="Roles">Admin and/or Supervisor. Agent is implied by belonging to at least one area.</param>
public record CreateUserRequest(string Email, string DisplayName, IReadOnlyCollection<string>? Roles, IReadOnlyCollection<int>? AreaIds);

public record UpdateUserRequest(string DisplayName, bool IsActive, IReadOnlyCollection<string>? Roles, IReadOnlyCollection<int>? AreaIds);

/// <summary>The temporary password is shown once, to the administrator who created or reset it.</summary>
public record UserWithPasswordDto(UserAdminDto User, string TemporaryPassword);

/// <summary>
/// Accounts created by an administrator, for installations without Microsoft 365 or for people outside it.
/// The user gets a temporary password and must choose their own on first sign-in.
/// </summary>
public static class UserAdminEndpoints
{
    private static readonly string[] AssignableRoles = [Roles.Admin, Roles.Supervisor];

    public static IEndpointRouteBuilder MapUserAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users").RequireAuthorization(p => p.RequireRole(Roles.Admin));
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapPost("/{id:guid}/reset-password", ResetPassword);
        return app;
    }

    private static async Task<IResult> List(HelpDeskDbContext db, CancellationToken ct)
    {
        var users = await db.Users.OrderBy(u => u.DisplayName).ThenBy(u => u.Email).ToListAsync(ct);
        return TypedResults.Ok(await ToDtosAsync(db, users, ct));
    }

    private static async Task<IResult> Create(
        CreateUserRequest request,
        UserManager<ApplicationUser> users,
        HelpDeskDbContext db,
        CancellationToken ct)
    {
        if (await ValidateAsync(db, request.Roles, request.AreaIds, ct) is { } invalid)
        {
            return invalid;
        }

        var email = request.Email.Trim();
        var password = TemporaryPassword.Generate();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName.Trim(),
            MustChangePassword = true,
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return Problem(created);
        }

        await ApplyRolesAndAreasAsync(users, db, user, request.Roles, request.AreaIds, ct);
        var dto = (await ToDtosAsync(db, [user], ct)).Single();
        return TypedResults.Created($"/api/admin/users/{user.Id}", new UserWithPasswordDto(dto, password));
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateUserRequest request,
        CurrentUser me,
        UserManager<ApplicationUser> users,
        HelpDeskDbContext db,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (await ValidateAsync(db, request.Roles, request.AreaIds, ct) is { } invalid)
        {
            return invalid;
        }

        // Keep at least the current administrator able to manage the system.
        if (user.Id == me.Id && (!request.IsActive || request.Roles?.Contains(Roles.Admin) != true))
        {
            return TypedResults.UnprocessableEntity(new { message = "No puedes quitarte el rol de administrador ni desactivarte a ti mismo." });
        }

        var deactivating = user.IsActive && !request.IsActive;
        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? user.Email ?? "" : request.DisplayName.Trim();
        user.IsActive = request.IsActive;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Problem(updated);
        }

        if (deactivating)
        {
            // Invalidates their existing sessions at the next cookie validation.
            await users.UpdateSecurityStampAsync(user);
        }

        await ApplyRolesAndAreasAsync(users, db, user, request.Roles, request.AreaIds, ct);
        return TypedResults.Ok((await ToDtosAsync(db, [user], ct)).Single());
    }

    private static async Task<IResult> ResetPassword(Guid id, UserManager<ApplicationUser> users, HelpDeskDbContext db, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        var password = TemporaryPassword.Generate();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded)
        {
            return Problem(reset);
        }

        user.MustChangePassword = true;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return TypedResults.Ok(new UserWithPasswordDto((await ToDtosAsync(db, [user], ct)).Single(), password));
    }

    private static async Task<IResult?> ValidateAsync(
        HelpDeskDbContext db,
        IReadOnlyCollection<string>? roles,
        IReadOnlyCollection<int>? areaIds,
        CancellationToken ct)
    {
        var unknownRoles = (roles ?? []).Except(AssignableRoles).ToList();
        if (unknownRoles.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = [$"Rol no válido: {string.Join(", ", unknownRoles)}."] });
        }

        var ids = (areaIds ?? []).Distinct().ToList();
        if (ids.Count > 0 && await db.Areas.CountAsync(a => ids.Contains(a.Id), ct) != ids.Count)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["areaIds"] = ["Alguna de las áreas no existe."] });
        }

        return null;
    }

    /// <summary>Makes the user's roles and area memberships match the request. Agent follows area membership.</summary>
    private static async Task ApplyRolesAndAreasAsync(
        UserManager<ApplicationUser> users,
        HelpDeskDbContext db,
        ApplicationUser user,
        IReadOnlyCollection<string>? roles,
        IReadOnlyCollection<int>? areaIds,
        CancellationToken ct)
    {
        var wantedAreas = (areaIds ?? []).Distinct().ToHashSet();
        var memberships = await db.AreaMembers.Where(m => m.UserId == user.Id).ToListAsync(ct);
        db.AreaMembers.RemoveRange(memberships.Where(m => !wantedAreas.Contains(m.AreaId)));
        db.AreaMembers.AddRange(wantedAreas
            .Where(areaId => memberships.All(m => m.AreaId != areaId))
            .Select(areaId => new AreaMember { AreaId = areaId, UserId = user.Id }));
        await db.SaveChangesAsync(ct);

        var wantedRoles = (roles ?? []).Distinct().ToHashSet();
        if (wantedAreas.Count > 0)
        {
            wantedRoles.Add(Roles.Agent);
        }

        var current = await users.GetRolesAsync(user);
        var toRemove = current.Except(wantedRoles).ToList();
        var toAdd = wantedRoles.Except(current).ToList();
        if (toRemove.Count > 0)
        {
            await users.RemoveFromRolesAsync(user, toRemove);
        }

        if (toAdd.Count > 0)
        {
            await users.AddToRolesAsync(user, toAdd);
        }
    }

    private static async Task<List<UserAdminDto>> ToDtosAsync(HelpDeskDbContext db, IReadOnlyCollection<ApplicationUser> users, CancellationToken ct)
    {
        var ids = users.Select(u => u.Id).ToList();
        var roles = await db.UserRoles
            .Where(ur => ids.Contains(ur.UserId))
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);
        var areas = await db.AreaMembers.Where(m => ids.Contains(m.UserId)).ToListAsync(ct);
        var microsoft = await db.UserLogins
            .Where(l => ids.Contains(l.UserId) && l.LoginProvider == MicrosoftLoginOptions.Scheme)
            .Select(l => l.UserId)
            .ToListAsync(ct);

        return users.Select(u => new UserAdminDto(
            u.Id,
            u.Email ?? "",
            u.DisplayName,
            u.IsActive,
            u.MustChangePassword,
            microsoft.Contains(u.Id),
            roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).OrderBy(n => n).ToList(),
            areas.Where(a => a.UserId == u.Id).Select(a => a.AreaId).OrderBy(a => a).ToList())).ToList();
    }

    private static IResult Problem(IdentityResult result) =>
        TypedResults.UnprocessableEntity(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
}
