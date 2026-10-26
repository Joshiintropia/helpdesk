using HelpDesk.Api.Auth;
using HelpDesk.Domain.Areas;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Endpoints;

public static class AreaEndpoints
{
    public static IEndpointRouteBuilder MapAreaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", GetMe).RequireAuthorization();

        var areas = app.MapGroup("/api/areas").RequireAuthorization();
        areas.MapGet("/", ListAreas);
        areas.MapPost("/", CreateArea).RequireAuthorization(p => p.RequireRole(Roles.Admin));
        areas.MapPut("/{areaId:int}", UpdateArea).RequireAuthorization(p => p.RequireRole(Roles.Admin));
        areas.MapPost("/{areaId:int}/members", AddMember).RequireAuthorization(p => p.RequireRole(Roles.Admin));
        return app;
    }

    private static async Task<IResult> GetMe(CurrentUser me, UserManager<ApplicationUser> users, HelpDeskDbContext db, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(me.Id.ToString());
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var roles = await users.GetRolesAsync(user);
        var areaIds = await db.AreaMembers.Where(m => m.UserId == user.Id).Select(m => m.AreaId).ToListAsync(ct);
        var areas = await db.Areas
            .Where(a => areaIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Select(a => new AreaDto(a.Id, a.Name, a.Description, a.AcceptsRequests))
            .ToListAsync(ct);

        return TypedResults.Ok(new MeDto(user.Id, user.Email ?? "", user.DisplayName, user.MustChangePassword, roles.ToList(), areas));
    }

    /// <summary>
    /// Active areas. By default only the ones that take requests (what the portal shows as
    /// "¿Con quién necesitas ayuda?"); <c>?all=true</c> lists every area, for escalation targets.
    /// </summary>
    private static async Task<IResult> ListAreas(HelpDeskDbContext db, bool all = false, CancellationToken ct = default)
    {
        var areas = await db.Areas
            .Where(a => a.IsActive && (all || a.AcceptsRequests))
            .OrderBy(a => a.Name)
            .Select(a => new AreaDto(a.Id, a.Name, a.Description, a.AcceptsRequests))
            .ToListAsync(ct);
        return TypedResults.Ok(areas);
    }

    private static async Task<IResult> CreateArea(CreateAreaRequest request, HelpDeskDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["El nombre es obligatorio."] });
        }

        var name = request.Name.Trim();
        if (await db.Areas.AnyAsync(a => a.Name == name, ct))
        {
            return TypedResults.Conflict(new { message = "Ya existe un área con ese nombre." });
        }

        var area = new Area { Name = name, Description = request.Description?.Trim(), AcceptsRequests = request.AcceptsRequests };
        db.Areas.Add(area);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/areas/{area.Id}", new AreaDto(area.Id, area.Name, area.Description, area.AcceptsRequests));
    }

    private static async Task<IResult> UpdateArea(int areaId, UpdateAreaRequest request, HelpDeskDbContext db, CancellationToken ct)
    {
        var area = await db.Areas.FindAsync([areaId], ct);
        if (area is null)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["El nombre es obligatorio."] });
        }

        var name = request.Name.Trim();
        if (await db.Areas.AnyAsync(a => a.Name == name && a.Id != areaId, ct))
        {
            return TypedResults.Conflict(new { message = "Ya existe un área con ese nombre." });
        }

        area.Name = name;
        area.Description = request.Description?.Trim();
        area.AcceptsRequests = request.AcceptsRequests;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new AreaDto(area.Id, area.Name, area.Description, area.AcceptsRequests));
    }

    private static async Task<IResult> AddMember(
        int areaId,
        AddAreaMemberRequest request,
        HelpDeskDbContext db,
        UserManager<ApplicationUser> users,
        CancellationToken ct)
    {
        if (!await db.Areas.AnyAsync(a => a.Id == areaId, ct))
        {
            return TypedResults.NotFound();
        }

        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return TypedResults.NotFound(new { message = "No existe un usuario con ese correo." });
        }

        var member = await db.AreaMembers.FindAsync([areaId, user.Id], ct);
        if (member is null)
        {
            db.AreaMembers.Add(new AreaMember { AreaId = areaId, UserId = user.Id, IsLead = request.IsLead });
        }
        else
        {
            member.IsLead = request.IsLead;
        }

        await db.SaveChangesAsync(ct);

        if (!await users.IsInRoleAsync(user, Roles.Agent))
        {
            await users.AddToRoleAsync(user, Roles.Agent);
        }

        return TypedResults.NoContent();
    }
}
