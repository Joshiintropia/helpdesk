using HelpDesk.Domain.Sla;
using HelpDesk.Domain.Tickets;

namespace HelpDesk.Api.Endpoints;

public record AreaDto(int Id, string Name, string? Description, bool AcceptsRequests);

public record CreateAreaRequest(string Name, string? Description, bool AcceptsRequests = true);

public record UpdateAreaRequest(string Name, string? Description, bool AcceptsRequests);

public record AddAreaMemberRequest(string Email, bool IsLead = false);

public record MeDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool MustChangePassword,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<AreaDto> Areas);

public record CreateTicketRequest(int AreaId, string Description, string? Title);

public record AddMessageRequest(string Body, MessageVisibility Visibility = MessageVisibility.Public);

public record EscalateRequest(Guid SegmentId, int ToAreaId, string Reason);

public record ResolveRequest(string? Note);

public record ChangePriorityRequest(TicketPriority Priority);

/// <summary>Where a segment stands against its SLA: which target counts now, when it is due, and how it is going.</summary>
public record SlaDto(SlaState State, SlaTarget? Target, DateTimeOffset? DueAt)
{
    public static SlaDto From(SlaStatus status) => new(status.State, status.Target, status.DueAt);
}

public record PersonDto(Guid Id, string DisplayName);

public record TicketSummaryDto(
    long Id,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    int CurrentAreaId,
    string CurrentAreaName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record SegmentDto(
    Guid Id,
    int AreaId,
    string AreaName,
    Guid? ParentSegmentId,
    SegmentStatus Status,
    string? EscalationReason,
    PersonDto? EscalatedBy,
    PersonDto? Assignee,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote,
    DateTimeOffset? FirstRespondedAt,
    DateTimeOffset? FirstResponseDueAt,
    DateTimeOffset? ResolutionDueAt,
    SlaDto Sla);

public record MessageDto(
    Guid Id,
    PersonDto Author,
    int? AuthorAreaId,
    string Body,
    MessageVisibility Visibility,
    MessageSource Source,
    DateTimeOffset CreatedAt);

/// <summary>
/// A ticket as one viewer may see it. Requesters get no segments and no internal notes:
/// the escalation chain is internal, they only see the status.
/// </summary>
public record TicketDetailDto(
    long Id,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    PersonDto Requester,
    int CurrentAreaId,
    string CurrentAreaName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyCollection<SegmentDto>? Segments,
    IReadOnlyCollection<MessageDto> Messages);

public record QueueItemDto(
    long TicketId,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    Guid SegmentId,
    int AreaId,
    Guid? AssigneeId,
    string? EscalationReason,
    DateTimeOffset OpenedAt,
    DateTimeOffset TicketCreatedAt,
    SlaDto Sla);

public record CalendarDto(
    int Id,
    string Name,
    string TimeZone,
    TimeOnly WorkdayStart,
    TimeOnly WorkdayEnd,
    IReadOnlyCollection<DayOfWeek> WorkingDays,
    IReadOnlyCollection<HolidayDto> Holidays);

public record HolidayDto(int Id, DateOnly Date, string Name);

public record UpdateCalendarRequest(string TimeZone, TimeOnly WorkdayStart, TimeOnly WorkdayEnd, IReadOnlyCollection<DayOfWeek> WorkingDays);

public record AddHolidayRequest(DateOnly Date, string Name);

/// <summary>Targets in business minutes. AreaId null is the general policy for that priority.</summary>
public record SlaPolicyDto(int Id, int? AreaId, TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes);

public record SaveSlaPolicyRequest(int? AreaId, TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes);

public record SlaSettingsDto(CalendarDto Calendar, IReadOnlyCollection<SlaPolicyDto> Policies);
