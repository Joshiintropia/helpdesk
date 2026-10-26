using System.Security.Claims;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Auth;

/// <summary>Who is calling, and which areas they work in. Resolved once per request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor, HelpDeskDbContext db)
{
    private IReadOnlyCollection<int>? _areaIds;

    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("No hay una petición HTTP activa.");

    public Guid Id => Guid.Parse(Principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("El usuario no está autenticado."));

    public bool IsAdmin => Principal.IsInRole(Roles.Admin);

    /// <summary>Supervisors and admins can act on any area.</summary>
    public bool CanActOnAnyArea => IsAdmin || Principal.IsInRole(Roles.Supervisor);

    public async Task<IReadOnlyCollection<int>> AreaIdsAsync(CancellationToken cancellationToken)
    {
        if (_areaIds is not null)
        {
            return _areaIds;
        }

        var userId = Id;
        var ids = await db.AreaMembers.Where(m => m.UserId == userId).Select(m => m.AreaId).ToListAsync(cancellationToken);
        if (CanActOnAnyArea)
        {
            ids = await db.Areas.Select(a => a.Id).ToListAsync(cancellationToken);
        }

        return _areaIds = ids;
    }

    public async Task<bool> WorksInAsync(int areaId, CancellationToken cancellationToken) =>
        (await AreaIdsAsync(cancellationToken)).Contains(areaId);
}
