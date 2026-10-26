using HelpDesk.Domain.Areas;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.Property(t => t.Id).UseIdentityAlwaysColumn();
        builder.Property(t => t.Title).HasMaxLength(Ticket.MaxTitleLength).IsRequired();
        builder.Property(t => t.Description).IsRequired();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.RequesterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Area>().WithMany().HasForeignKey(t => t.CurrentAreaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Segments).WithOne(s => s.Ticket).HasForeignKey(s => s.TicketId);
        builder.HasMany(t => t.Messages).WithOne(m => m.Ticket).HasForeignKey(m => m.TicketId);
        builder.HasMany(t => t.Events).WithOne(e => e.Ticket).HasForeignKey(e => e.TicketId);

        builder.Ignore(t => t.RootSegment);

        builder.HasIndex(t => new { t.RequesterId, t.CreatedAt });
        builder.HasIndex(t => new { t.Status, t.CurrentAreaId });
    }
}

internal sealed class TicketSegmentConfiguration : IEntityTypeConfiguration<TicketSegment>
{
    public void Configure(EntityTypeBuilder<TicketSegment> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.EscalationReason).HasMaxLength(2000);
        builder.Property(s => s.ResolutionNote).HasMaxLength(4000);

        builder.HasOne<Area>().WithMany().HasForeignKey(s => s.AreaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketSegment>().WithMany().HasForeignKey(s => s.ParentSegmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.EscalatedById).OnDelete(DeleteBehavior.Restrict);

        // The agent queue: open segments of an area.
        builder.HasIndex(s => new { s.AreaId, s.Status });
    }
}

internal sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> builder)
    {
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Body).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => new { m.TicketId, m.CreatedAt });
    }
}

internal sealed class TicketEventConfiguration : IEntityTypeConfiguration<TicketEvent>
{
    public void Configure(EntityTypeBuilder<TicketEvent> builder)
    {
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.FromValue).HasMaxLength(200);
        builder.Property(e => e.ToValue).HasMaxLength(200);
        builder.HasIndex(e => new { e.TicketId, e.OccurredAt });
        builder.HasIndex(e => new { e.Type, e.OccurredAt });
    }
}
