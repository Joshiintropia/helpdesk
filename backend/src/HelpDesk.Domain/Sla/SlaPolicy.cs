using HelpDesk.Domain.Tickets;

namespace HelpDesk.Domain.Sla;

/// <summary>
/// Target times for one priority. A policy with an AreaId applies to that area's segments;
/// a policy without one applies to the ticket as a whole.
/// </summary>
public class SlaPolicy
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? AreaId { get; set; }
    public TicketPriority Priority { get; set; }
    public int FirstResponseMinutes { get; set; }
    public int ResolutionMinutes { get; set; }
    public int CalendarId { get; set; }
}

/// <summary>Working hours used to count SLA time (only business minutes count).</summary>
public class BusinessCalendar
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string TimeZone { get; set; } = "America/Mexico_City";
    public TimeOnly WorkdayStart { get; set; } = new(9, 0);
    public TimeOnly WorkdayEnd { get; set; } = new(18, 0);
    public WorkingDays WorkingDays { get; set; } = WorkingDays.MondayToFriday;
    public List<Holiday> Holidays { get; set; } = [];
}

public class Holiday
{
    public int Id { get; set; }
    public int CalendarId { get; set; }
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
}

[Flags]
public enum WorkingDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    MondayToFriday = Monday | Tuesday | Wednesday | Thursday | Friday,
}
