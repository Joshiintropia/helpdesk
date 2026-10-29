using HelpDesk.Domain.Areas;
using HelpDesk.Domain.Sla;
using HelpDesk.Domain.Tickets;
using HelpDesk.Infrastructure.Attachments;
using HelpDesk.Infrastructure.Email;
using HelpDesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistence;

public class HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<AreaMember> AreaMembers => Set<AreaMember>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketSegment> TicketSegments => Set<TicketSegment>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<TicketEvent> TicketEvents => Set<TicketEvent>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<BusinessCalendar> BusinessCalendars => Set<BusinessCalendar>();
    public DbSet<ProcessedEmail> ProcessedEmails => Set<ProcessedEmail>();
    public DbSet<EmailNotification> EmailNotifications => Set<EmailNotification>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HelpDeskDbContext).Assembly);

        // Identity defaults to "AspNetUsers"-style names; keep the whole schema in snake_case.
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<Holiday>().ToTable("holidays");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums are stored as text so the database stays readable for reports and BI tools.
        builder.Properties<TicketStatus>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<TicketPriority>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<SegmentStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<MessageVisibility>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<MessageSource>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<TicketEventType>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<InboundOutcome>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<NotificationKind>().HaveConversion<string>().HaveMaxLength(16);
    }
}
