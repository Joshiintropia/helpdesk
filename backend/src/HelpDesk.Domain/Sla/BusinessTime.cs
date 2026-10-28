namespace HelpDesk.Domain.Sla;

/// <summary>
/// Counts SLA time in business minutes: only the calendar's working hours on working days that are not
/// holidays, in the calendar's time zone. A ticket opened on Friday at 17:30 with a 2-hour target is due
/// on Monday at 10:30.
/// </summary>
public sealed class BusinessTime
{
    // Ten years of days: a guard against a calendar with no working time at all.
    private const int MaxDays = 3660;

    private readonly TimeZoneInfo _zone;
    private readonly TimeOnly _start;
    private readonly TimeOnly _end;
    private readonly WorkingDays _days;
    private readonly HashSet<DateOnly> _holidays;

    public BusinessTime(BusinessCalendar calendar)
    {
        if (calendar.WorkdayEnd <= calendar.WorkdayStart)
        {
            throw new DomainException("La hora de salida debe ser después de la hora de entrada.");
        }

        if (calendar.WorkingDays == WorkingDays.None)
        {
            throw new DomainException("Elige al menos un día laboral.");
        }

        _zone = FindZone(calendar.TimeZone);
        _start = calendar.WorkdayStart;
        _end = calendar.WorkdayEnd;
        _days = calendar.WorkingDays;
        _holidays = calendar.Holidays.Select(h => h.Date).ToHashSet();
    }

    /// <summary>The moment that is <paramref name="minutes"/> business minutes after <paramref name="from"/>.</summary>
    public DateTimeOffset Add(DateTimeOffset from, int minutes)
    {
        var local = TimeZoneInfo.ConvertTime(from, _zone).DateTime;
        var remaining = (double)Math.Max(0, minutes);

        for (var i = 0; i < MaxDays; i++)
        {
            var day = DateOnly.FromDateTime(local);
            if (IsWorkingDay(day))
            {
                var open = day.ToDateTime(_start);
                var close = day.ToDateTime(_end);
                var begin = local > open ? local : open;
                if (begin < close)
                {
                    var available = (close - begin).TotalMinutes;
                    if (remaining <= available)
                    {
                        return ToUtc(begin.AddMinutes(remaining));
                    }

                    remaining -= available;
                }
            }

            local = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        }

        throw new DomainException("El calendario no tiene horas laborales suficientes para calcular el vencimiento.");
    }

    /// <summary>Business minutes between two moments; zero when <paramref name="to"/> is not after <paramref name="from"/>.</summary>
    public int MinutesBetween(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            return 0;
        }

        var local = TimeZoneInfo.ConvertTime(from, _zone).DateTime;
        var end = TimeZoneInfo.ConvertTime(to, _zone).DateTime;
        var total = 0.0;

        for (var i = 0; i < MaxDays && local < end; i++)
        {
            var day = DateOnly.FromDateTime(local);
            if (IsWorkingDay(day))
            {
                var open = day.ToDateTime(_start);
                var close = day.ToDateTime(_end);
                var begin = local > open ? local : open;
                var finish = end < close ? end : close;
                if (finish > begin)
                {
                    total += (finish - begin).TotalMinutes;
                }
            }

            local = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        }

        return (int)Math.Round(total);
    }

    public bool IsWorkingDay(DateOnly day) => _days.HasFlag(ToFlag(day.DayOfWeek)) && !_holidays.Contains(day);

    private DateTimeOffset ToUtc(DateTime local)
    {
        // A local time skipped by a daylight-saving change does not exist; move past the gap.
        while (_zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone), TimeSpan.Zero);
    }

    public static bool IsValidZone(string id)
    {
        try
        {
            FindZone(id);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    private static TimeZoneInfo FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException($"No se reconoce la zona horaria \"{id}\".");
        }
    }

    public static WorkingDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WorkingDays.Monday,
        DayOfWeek.Tuesday => WorkingDays.Tuesday,
        DayOfWeek.Wednesday => WorkingDays.Wednesday,
        DayOfWeek.Thursday => WorkingDays.Thursday,
        DayOfWeek.Friday => WorkingDays.Friday,
        DayOfWeek.Saturday => WorkingDays.Saturday,
        _ => WorkingDays.Sunday,
    };
}
