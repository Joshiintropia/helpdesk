using HelpDesk.Domain;
using HelpDesk.Domain.Sla;

namespace HelpDesk.Tests.Domain;

public class BusinessTimeTests
{
    // Mexico City has no daylight saving time since 2022: always UTC-6.
    private static readonly TimeSpan Mexico = TimeSpan.FromHours(-6);

    private static BusinessTime Calendar(params Holiday[] holidays) =>
        new(new BusinessCalendar { Name = "Oficina", Holidays = [.. holidays] });

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, Mexico);

    [Fact]
    public void Time_inside_the_workday_counts_directly()
    {
        // Wednesday 28 Oct 10:00 + 2 h.
        Assert.Equal(At(10, 28, 12), Calendar().Add(At(10, 28, 10), 120));
    }

    [Fact]
    public void A_friday_evening_ticket_is_due_on_monday()
    {
        // Friday 30 Oct 17:30 + 2 h: 30 min on Friday, 90 min on Monday from 9:00.
        Assert.Equal(At(11, 2, 10, 30), Calendar().Add(At(10, 30, 17, 30), 120));
    }

    [Fact]
    public void Time_before_opening_starts_counting_at_opening()
    {
        Assert.Equal(At(10, 28, 9, 30), Calendar().Add(At(10, 28, 6), 30));
    }

    [Fact]
    public void Holidays_are_skipped()
    {
        // Monday 16 Nov 2026 is the Revolution Day holiday; Friday 13 Nov 17:00 + 2 h lands on Tuesday.
        var calendar = Calendar(new Holiday { Date = new DateOnly(2026, 11, 16), Name = "Día de la Revolución" });
        Assert.Equal(At(11, 17, 10), calendar.Add(At(11, 13, 17), 120));
    }

    [Fact]
    public void Minutes_between_skip_nights_and_weekends()
    {
        // Friday 17:00 to Monday 10:00: one hour on Friday and one on Monday.
        Assert.Equal(120, Calendar().MinutesBetween(At(10, 30, 17), At(11, 2, 10)));
        Assert.Equal(0, Calendar().MinutesBetween(At(10, 31, 10), At(11, 1, 20)));
    }

    [Fact]
    public void Adding_and_measuring_agree()
    {
        var calendar = Calendar();
        var start = At(10, 29, 16, 45);
        Assert.Equal(1000, calendar.MinutesBetween(start, calendar.Add(start, 1000)));
    }

    [Fact]
    public void A_calendar_without_working_time_is_rejected()
    {
        Assert.Throws<DomainException>(() => new BusinessTime(new BusinessCalendar { Name = "x", WorkingDays = WorkingDays.None }));
        Assert.Throws<DomainException>(() => new BusinessTime(new BusinessCalendar { Name = "x", WorkdayStart = new(18, 0), WorkdayEnd = new(9, 0) }));
        Assert.Throws<DomainException>(() => new BusinessTime(new BusinessCalendar { Name = "x", TimeZone = "Marte/Olympus" }));
    }
}
