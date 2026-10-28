using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Email;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Endpoints;

/// <summary>The satisfaction survey from the email link, without signing in. The signed token is the access.</summary>
public static class SurveyEndpoints
{
    public static IEndpointRouteBuilder MapSurveyEndpoints(this IEndpointRouteBuilder app)
    {
        var survey = app.MapGroup("/api/survey").AllowAnonymous();
        survey.MapGet("/{token}", Get);
        survey.MapPost("/{token}", Rate);
        return app;
    }

    private static async Task<IResult> Get(string token, SurveyLinks links, HelpDeskDbContext db, CancellationToken ct)
    {
        var ticket = await FindAsync(token, links, db, ct);
        return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(ticket));
    }

    private static async Task<IResult> Rate(string token, RateRequest request, SurveyLinks links, HelpDeskDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var ticket = await FindAsync(token, links, db, ct);
        if (ticket is null)
        {
            return TypedResults.NotFound();
        }

        ticket.Rate(request.Rating, request.Comment, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(ticket));
    }

    private static async Task<Ticket?> FindAsync(string token, SurveyLinks links, HelpDeskDbContext db, CancellationToken ct) =>
        links.TicketIdFrom(token) is { } id ? await db.Tickets.SingleOrDefaultAsync(t => t.Id == id, ct) : null;

    private static SurveyDto ToDto(Ticket ticket) => new(
        ticket.Id,
        ticket.Title,
        ticket.Status,
        CanRate: ticket.Status == TicketStatus.Resolved || (ticket.Status == TicketStatus.Closed && ticket.RatedAt is null),
        ticket.SatisfactionRating,
        ticket.SatisfactionComment);
}
