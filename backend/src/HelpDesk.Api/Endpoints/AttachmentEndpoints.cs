using HelpDesk.Api.Auth;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Attachments;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace HelpDesk.Api.Endpoints;

/// <summary>
/// Files are uploaded one by one first, then sent with a new request or message by id. Downloads check the
/// same visibility as the message they came with, so an internal note's files stay internal.
/// </summary>
public static class AttachmentEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var files = app.MapGroup("/api/attachments").RequireAuthorization();
        // Cookie-authenticated multipart: SameSite=Lax already keeps other sites from posting with the cookie.
        files.MapPost("/", Upload).DisableAntiforgery();
        files.MapGet("/{id:guid}", Download);
        return app;
    }

    private static async Task<IResult> Upload(IFormFile file, CurrentUser me, AttachmentService attachments, HelpDeskDbContext db, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var attachment = await attachments.UploadAsync(content, file.FileName, file.ContentType, file.Length, me.Id, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new AttachmentDto(
            attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, AttachmentRules.IsImage(attachment.ContentType)));
    }

    private static async Task<IResult> Download(Guid id, CurrentUser me, HelpDeskDbContext db, AttachmentStore store, CancellationToken ct)
    {
        var attachment = await db.TicketAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null || !await CanSeeAsync(attachment, me, db, ct))
        {
            return TypedResults.NotFound();
        }

        Stream content;
        try
        {
            content = store.Open(attachment.StorageKey);
        }
        catch (FileNotFoundException)
        {
            return TypedResults.NotFound();
        }

        // Images display inline (previews); everything else is a download, and nosniff keeps the
        // browser from treating it as anything but the declared type.
        var image = AttachmentRules.IsImage(attachment.ContentType);
        return TypedResults.Stream(
            content,
            attachment.ContentType,
            fileDownloadName: image ? null : attachment.FileName,
            lastModified: attachment.CreatedAt,
            entityTag: new EntityTagHeaderValue($"\"{attachment.Id:N}\""));
    }

    private static async Task<bool> CanSeeAsync(TicketAttachment attachment, CurrentUser me, HelpDeskDbContext db, CancellationToken ct)
    {
        if (attachment.TicketId is not { } ticketId)
        {
            return attachment.UploadedById == me.Id;
        }

        var ticket = await db.Tickets.AsNoTracking().Include(t => t.Segments).SingleAsync(t => t.Id == ticketId, ct);
        var myAreas = await me.AreaIdsAsync(ct);
        var isAgent = ticket.Segments.Any(s => myAreas.Contains(s.AreaId));
        if (ticket.RequesterId != me.Id && !isAgent)
        {
            return false;
        }

        if (attachment.MessageId is not { } messageId)
        {
            return true;
        }

        var message = await db.TicketMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId, ct);
        return message is not null && message.IsVisibleTo(isAgent ? myAreas : []);
    }
}
