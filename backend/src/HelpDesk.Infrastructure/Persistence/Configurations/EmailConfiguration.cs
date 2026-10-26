using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedEmailConfiguration : IEntityTypeConfiguration<ProcessedEmail>
{
    public void Configure(EntityTypeBuilder<ProcessedEmail> builder)
    {
        builder.Property(p => p.Id).UseIdentityAlwaysColumn();
        builder.Property(p => p.MessageId).HasMaxLength(998).IsRequired();
        builder.Property(p => p.FromAddress).HasMaxLength(320).IsRequired();
        builder.Property(p => p.Subject).HasMaxLength(500).IsRequired();
        builder.HasOne<Ticket>().WithMany().HasForeignKey(p => p.TicketId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(p => p.MessageId).IsUnique();
    }
}

internal sealed class EmailNotificationConfiguration : IEntityTypeConfiguration<EmailNotification>
{
    public void Configure(EntityTypeBuilder<EmailNotification> builder)
    {
        builder.Property(n => n.Id).UseIdentityAlwaysColumn();
        builder.Property(n => n.LastError).HasMaxLength(1000);
        builder.HasOne(n => n.Ticket).WithMany().HasForeignKey(n => n.TicketId).OnDelete(DeleteBehavior.Cascade);

        // The worker only looks at what is still pending.
        builder.HasIndex(n => n.Id).HasFilter("sent_at IS NULL").HasDatabaseName("ix_email_notifications_pending");
    }
}
