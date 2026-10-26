using HelpDesk.Domain.Areas;
using HelpDesk.Domain.Sla;
using HelpDesk.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistence.Configurations;

internal sealed class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(500);
        builder.HasIndex(a => a.Name).IsUnique();
        builder.HasMany(a => a.Members).WithOne().HasForeignKey(m => m.AreaId);
        builder.HasMany(a => a.Categories).WithOne().HasForeignKey(c => c.AreaId);
    }
}

internal sealed class AreaMemberConfiguration : IEntityTypeConfiguration<AreaMember>
{
    public void Configure(EntityTypeBuilder<AreaMember> builder)
    {
        builder.HasKey(m => new { m.AreaId, m.UserId });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId);
        builder.HasIndex(m => m.UserId);
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SlaConfiguration :
    IEntityTypeConfiguration<SlaPolicy>,
    IEntityTypeConfiguration<BusinessCalendar>,
    IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.HasOne<Area>().WithMany().HasForeignKey(p => p.AreaId);
        builder.HasOne<BusinessCalendar>().WithMany().HasForeignKey(p => p.CalendarId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.AreaId, p.Priority }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<BusinessCalendar> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.TimeZone).HasMaxLength(64).IsRequired();
        builder.HasMany(c => c.Holidays).WithOne().HasForeignKey(h => h.CalendarId);
    }

    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.Property(h => h.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(h => new { h.CalendarId, h.Date }).IsUnique();
    }
}
