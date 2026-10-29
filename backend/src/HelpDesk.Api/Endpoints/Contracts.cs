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

public record CreateTicketRequest(int AreaId, string Description, string? Title, IReadOnlyCollection<Guid>? AttachmentIds = null);

public record AddMessageRequest(string? Body, MessageVisibility Visibility = MessageVisibility.Public, IReadOnlyCollection<Guid>? AttachmentIds = null);

public record AttachmentDto(Guid Id, string FileName, string ContentType, long Size, bool IsImage);

public record RateRequest(int Rating, string? Comment);

/// <summary>The requester's rating, shown on the ticket.</summary>
public record SatisfactionDto(int Rating, string? Comment, DateTimeOffset RatedAt);

/// <summary>What the public survey page needs: no conversation, no people, only the ticket it rates.</summary>
public record SurveyDto(long TicketId, string Title, TicketStatus Status, bool CanRate, int? Rating, string? Comment);

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
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<AttachmentDto> Attachments);

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
    IReadOnlyCollection<MessageDto> Messages,
    IReadOnlyCollection<AttachmentDto> Attachments,
    SatisfactionDto? Satisfaction);

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

/// <summary>Met and decided counts rather than percentages, so the screen can say "18 de 20" and avoid 0 of 0.</summary>
public record SlaComplianceDto(
    int FirstResponseMet,
    int FirstResponseTotal,
    int ResolutionMet,
    int ResolutionTotal,
    int? MedianFirstResponseMinutes,
    int? MedianResolutionMinutes);

public record MetricsTotalsDto(int Received, int Resolved, int Open, int Overdue, SlaComplianceDto Sla, SatisfactionSummaryDto Satisfaction);

/// <summary>Survey answers given in the period. <c>ByRating[0]</c> counts one-star answers, <c>ByRating[4]</c> five stars.</summary>
public record SatisfactionSummaryDto(int Responses, double? Average, IReadOnlyList<int> ByRating);

public record DailyCountDto(DateOnly Date, int Received, int Resolved);

/// <summary>Open segments of an area by where they stand against their SLA.</summary>
public record BacklogDto(int OnTrack, int AtRisk, int Breached, int Paused, int NoSla);

public record AreaMetricsDto(
    int AreaId,
    string Name,
    int Received,
    int Resolved,
    int Open,
    int EscalatedOut,
    SlaComplianceDto Sla,
    BacklogDto Backlog,
    SatisfactionSummaryDto Satisfaction);

public record EscalationFlowDto(int FromAreaId, string FromName, int ToAreaId, string ToName, int Count);

public record MetricsDto(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    MetricsTotalsDto Totals,
    IReadOnlyCollection<DailyCountDto> Daily,
    IReadOnlyCollection<AreaMetricsDto> Areas,
    IReadOnlyCollection<EscalationFlowDto> Escalations);
