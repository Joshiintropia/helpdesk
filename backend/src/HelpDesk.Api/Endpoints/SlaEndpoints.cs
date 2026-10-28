using HelpDesk.Domain.Sla;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Endpoints;

/// <summary>
/// Working hours, holidays and target times. Changes apply to segments opened from now on and to
/// tickets whose priority changes; due dates already set are kept, so nobody's clock jumps.
/// </summary>
public static class SlaEndpoints
{
    public static IEndpointRouteBuilder MapSlaEndpoints(this IEndpointRouteBuilder app)
    {
        var sla = app.MapGroup("/api/sla").RequireAuthorization(p => p.RequireRole(Roles.Admin));
        sla.MapGet("/", Get);
        sla.MapPut("/calendar", UpdateCalendar);
        sla.MapPost("/calendar/holidays", AddHoliday);
        sla.MapDelete("/calendar/holidays/{holidayId:int}", DeleteHoliday);
        sla.MapPut("/policies", SavePolicies);
        return app;
    }

    private static async Task<IResult> Get(HelpDeskDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await SettingsAsync(db, ct));

    private static async Task<IResult> UpdateCalendar(UpdateCalendarRequest request, HelpDeskDbContext db, CancellationToken ct)
    {
        var calendar = await CalendarAsync(db, ct);
        calendar.TimeZone = request.TimeZone.Trim();
        calendar.WorkdayStart = request.WorkdayStart;
        calendar.WorkdayEnd = request.WorkdayEnd;
        calendar.WorkingDays = request.WorkingDays.Aggregate(WorkingDays.None, (days, day) => days | BusinessTime.ToFlag(day));

        // Throws a readable DomainException (422) for an unknown zone, no working days or reversed hours.
        _ = new BusinessTime(calendar);

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await SettingsAsync(db, ct));
    }

    private static async Task<IResult> AddHoliday(AddHolidayRequest request, HelpDeskDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Escribe el nombre del día festivo."] });
        }

        var calendar = await CalendarAsync(db, ct);
        if (calendar.Holidays.Any(h => h.Date == request.Date))
        {
            return TypedResults.Conflict(new { message = "Ese día ya está registrado como festivo." });
        }

        calendar.Holidays.Add(new Holiday { Date = request.Date, Name = request.Name.Trim() });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await SettingsAsync(db, ct));
    }

    private static async Task<IResult> DeleteHoliday(int holidayId, HelpDeskDbContext db, CancellationToken ct)
    {
        var calendar = await CalendarAsync(db, ct);
        var holiday = calendar.Holidays.SingleOrDefault(h => h.Id == holidayId);
        if (holiday is null)
        {
            return TypedResults.NotFound();
        }

        calendar.Holidays.Remove(holiday);
        db.Remove(holiday);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await SettingsAsync(db, ct));
    }

    /// <summary>Replaces every policy with the list sent: general ones (no area) and per-area overrides.</summary>
    private static async Task<IResult> SavePolicies(IReadOnlyCollection<SaveSlaPolicyRequest> request, HelpDeskDbContext db, CancellationToken ct)
    {
        var errors = new List<string>();
        if (request.GroupBy(p => (p.AreaId, p.Priority)).Any(g => g.Count() > 1))
        {
            errors.Add("Hay más de un tiempo para la misma área y prioridad.");
        }

        if (request.Any(p => p.FirstResponseMinutes <= 0 || p.ResolutionMinutes <= 0))
        {
            errors.Add("Los tiempos deben ser mayores a cero.");
        }

        if (request.Any(p => p.FirstResponseMinutes > p.ResolutionMinutes))
        {
            errors.Add("La primera respuesta no puede tardar más que la resolución.");
        }

        var areaIds = request.Select(p => p.AreaId).OfType<int>().Distinct().ToList();
        if (await db.Areas.CountAsync(a => areaIds.Contains(a.Id), ct) != areaIds.Count)
        {
            errors.Add("Alguna de las áreas no existe.");
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["policies"] = errors.ToArray() });
        }

        var calendar = await CalendarAsync(db, ct);
        var existing = await db.SlaPolicies.ToListAsync(ct);
        db.SlaPolicies.RemoveRange(existing.Where(e => !request.Any(r => r.AreaId == e.AreaId && r.Priority == e.Priority)));
        foreach (var item in request)
        {
            var policy = existing.SingleOrDefault(e => e.AreaId == item.AreaId && e.Priority == item.Priority);
            if (policy is null)
            {
                policy = new SlaPolicy { Name = PriorityName(item.Priority), AreaId = item.AreaId, Priority = item.Priority, CalendarId = calendar.Id };
                db.SlaPolicies.Add(policy);
            }

            policy.FirstResponseMinutes = item.FirstResponseMinutes;
            policy.ResolutionMinutes = item.ResolutionMinutes;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await SettingsAsync(db, ct));
    }

    /// <summary>The installation's calendar. There is one for now; policies point to it by id so more can come later.</summary>
    private static async Task<BusinessCalendar> CalendarAsync(HelpDeskDbContext db, CancellationToken ct) =>
        await db.BusinessCalendars.Include(c => c.Holidays).OrderBy(c => c.Id).FirstAsync(ct);

    private static async Task<SlaSettingsDto> SettingsAsync(HelpDeskDbContext db, CancellationToken ct)
    {
        var calendar = await CalendarAsync(db, ct);
        var policies = await db.SlaPolicies
            .OrderBy(p => p.AreaId.HasValue).ThenBy(p => p.AreaId).ThenBy(p => p.Priority)
            .Select(p => new SlaPolicyDto(p.Id, p.AreaId, p.Priority, p.FirstResponseMinutes, p.ResolutionMinutes))
            .ToListAsync(ct);

        return new SlaSettingsDto(
            new CalendarDto(
                calendar.Id,
                calendar.Name,
                calendar.TimeZone,
                calendar.WorkdayStart,
                calendar.WorkdayEnd,
                Enum.GetValues<DayOfWeek>().Where(d => calendar.WorkingDays.HasFlag(BusinessTime.ToFlag(d))).ToList(),
                calendar.Holidays.OrderBy(h => h.Date).Select(h => new HolidayDto(h.Id, h.Date, h.Name)).ToList()),
            policies);
    }

    private static string PriorityName(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => "Urgente",
        TicketPriority.High => "Alta",
        TicketPriority.Low => "Baja",
        _ => "Normal",
    };
}
