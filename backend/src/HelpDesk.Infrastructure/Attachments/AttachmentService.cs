using HelpDesk.Domain;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Attachments;

/// <summary>Uploads, links and cleans up attachments. Callers save the DbContext.</summary>
public sealed class AttachmentService(
    HelpDeskDbContext db,
    AttachmentStore store,
    IOptions<StorageOptions> options,
    TimeProvider clock,
    ILogger<AttachmentService> logger)
{
    /// <summary>Stores a file not yet linked to a ticket; only its uploader can link or see it.</summary>
    public async Task<TicketAttachment> UploadAsync(Stream content, string? fileName, string? contentType, long size, Guid uploaderId, CancellationToken cancellationToken)
    {
        var name = AttachmentRules.CleanName(fileName);
        if (AttachmentRules.Problem(name, size, options.Value.MaxFileBytes) is { } problem)
        {
            throw new DomainException(problem);
        }

        var now = clock.GetUtcNow();
        var attachment = new TicketAttachment
        {
            Id = Guid.CreateVersion7(now),
            FileName = name,
            ContentType = AttachmentRules.StoredType(contentType),
            Size = size,
            StorageKey = await store.SaveAsync(content, now, cancellationToken),
            UploadedById = uploaderId,
            CreatedAt = now,
        };
        db.TicketAttachments.Add(attachment);
        return attachment;
    }

    /// <summary>Links the caller's own pending uploads to a ticket, and to the message they came with.</summary>
    public async Task LinkAsync(IReadOnlyCollection<Guid>? ids, Guid uploaderId, Ticket ticket, Guid? messageId, CancellationToken cancellationToken)
    {
        if (ids is null || ids.Count == 0)
        {
            return;
        }

        if (ids.Count > AttachmentRules.MaxFilesPerMessage)
        {
            throw new DomainException($"Puedes adjuntar hasta {AttachmentRules.MaxFilesPerMessage} archivos por mensaje.");
        }

        var pending = await db.TicketAttachments
            .Where(a => ids.Contains(a.Id) && a.UploadedById == uploaderId && a.TicketId == null)
            .ToListAsync(cancellationToken);
        if (pending.Count != ids.Distinct().Count())
        {
            throw new DomainException("Alguno de los archivos ya no está disponible; vuelve a adjuntarlo.");
        }

        foreach (var attachment in pending)
        {
            attachment.Ticket = ticket;
            attachment.MessageId = messageId;
        }
    }

    /// <summary>Files that arrived with an email, linked right away. Rejected files are skipped and logged.</summary>
    public async Task AddFromEmailAsync(IReadOnlyCollection<InboundFile> files, Guid senderId, Ticket ticket, Guid? messageId, CancellationToken cancellationToken)
    {
        foreach (var file in files.Take(AttachmentRules.MaxFilesPerMessage * 2))
        {
            try
            {
                await using var content = new MemoryStream(file.Content, writable: false);
                var attachment = await UploadAsync(content, file.FileName, file.ContentType, file.Content.Length, senderId, cancellationToken);
                attachment.Ticket = ticket;
                attachment.MessageId = messageId;
            }
            catch (DomainException error)
            {
                logger.LogInformation("Adjunto omitido del correo del ticket {TicketId}: {Reason}", ticket.Id, error.Message);
            }
        }
    }

    /// <summary>Deletes uploads that were never sent with a ticket or message.</summary>
    public async Task<int> CleanupAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        var limit = clock.GetUtcNow() - olderThan;
        var stale = await db.TicketAttachments.Where(a => a.TicketId == null && a.CreatedAt < limit).ToListAsync(cancellationToken);
        foreach (var attachment in stale)
        {
            store.Delete(attachment.StorageKey);
        }

        db.TicketAttachments.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}

/// <summary>A file attached to an inbound email.</summary>
public sealed record InboundFile(string FileName, string? ContentType, byte[] Content);
