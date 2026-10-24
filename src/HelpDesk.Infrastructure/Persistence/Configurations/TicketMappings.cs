using HelpDesk.Domain.Common;
using HelpDesk.Domain.Entities.Delegations;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistence.Configurations;

internal static class TicketMappings
{
    internal static EntityTypeBuilder<T> Entity<T>(ModelBuilder builder, string table) where T : Entity
    {
        var entity = builder.Entity<T>();
        entity.ToTable(table);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedOnAdd();
        return entity;
    }

    public static void Configure(ModelBuilder builder)
    {
        var ticket = Entity<Ticket>(builder, "Tickets");
        ticket.Property(x => x.TicketNumber).HasMaxLength(32).IsRequired();
        ticket.Property(x => x.Title).HasMaxLength(200).IsRequired();
        ticket.Property(x => x.Description).IsRequired();
        ticket.Property(x => x.Category).HasConversion<byte>();
        ticket.Property(x => x.Priority).HasConversion<byte>();
        ticket.HasIndex(x => x.TicketNumber).IsUnique();
        ticket.HasIndex(x => new { x.RequestedByUserId, x.CreatedAtUtc });
        ticket.HasIndex(x => new { x.AssignedToUserId, x.CreatedAtUtc });
        ticket.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedByUserId);
        ticket.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedToUserId);
        ticket.ToTable("Tickets", t =>
        {
            t.HasCheckConstraint("CK_Tickets_Category", "[Category] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Tickets_Priority", "[Priority] BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_Tickets_AssignmentVersion", "[AssignmentVersion] >= 0");
        });

        var message = Entity<TicketMessage>(builder, "TicketMessages");
        message.Property(x => x.MessageType).HasConversion<byte>();
        message.Property(x => x.Body).IsRequired();
        message.HasAlternateKey(x => new { x.TicketId, x.Id });
        message.HasIndex(x => new { x.TicketId, x.CreatedAtUtc, x.Id });
        message.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId);
        message.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorUserId);
        message.ToTable("TicketMessages", t => t.HasCheckConstraint("CK_Messages_Type", "[MessageType] IN (1,2)"));

        var attachment = Entity<Attachment>(builder, "Attachments");
        attachment.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        attachment.Property(x => x.StorageKey).HasMaxLength(450).IsRequired();
        attachment.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        attachment.HasIndex(x => x.StorageKey).IsUnique();
        attachment.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId);
        attachment.HasOne<TicketMessage>().WithMany()
            .HasForeignKey(x => new { x.TicketId, x.TicketMessageId })
            .HasPrincipalKey(x => new { x.TicketId, x.Id });
        attachment.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedByUserId);
        attachment.ToTable("Attachments", t => t.HasCheckConstraint("CK_Attachments_Size", "[SizeBytes] >= 0"));

        var delegation = Entity<Delegation>(builder, "Delegations");
        delegation.Property(x => x.Reason).HasMaxLength(1000);
        delegation.HasIndex(x => new { x.SourceUserId, x.StartsAtUtc, x.EndsAtUtc });
        delegation.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SourceUserId);
        delegation.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TargetUserId);
        delegation.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId);
        delegation.ToTable("Delegations", t =>
        {
            t.HasCheckConstraint("CK_Delegations_DifferentUsers", "[SourceUserId] <> [TargetUserId]");
            t.HasCheckConstraint("CK_Delegations_Period", "[StartsAtUtc] < [EndsAtUtc]");
        });

        var delegated = Entity<DelegationTicket>(builder, "DelegationTickets");
        delegated.Property(x => x.EntryReason).HasConversion<string>().HasMaxLength(32);
        delegated.Property(x => x.ReturnOutcome).HasConversion<string>().HasMaxLength(32);
        delegated.HasIndex(x => new { x.DelegationId, x.TicketId }).IsUnique();
        delegated.HasIndex(x => new { x.DelegationId, x.ReturnOutcome });
        delegated.HasOne<Delegation>().WithMany().HasForeignKey(x => x.DelegationId);
        delegated.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId);
        delegated.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OriginalAssigneeUserId);
        delegated.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DelegatedToUserId);
        delegated.ToTable("DelegationTickets", t =>
        {
            t.HasCheckConstraint("CK_DelegationTickets_Version", "[DelegatedAssignmentVersion] >= 0");
            t.HasCheckConstraint("CK_DelegationTickets_Reason", "[EntryReason] IN ('ExistingTicket','NewAssignment')");
            t.HasCheckConstraint("CK_DelegationTickets_Outcome",
                "[ReturnOutcome] IN ('AwaitingEnd','Returned','SkippedClosed','SkippedReassigned')");
        });

        var history = Entity<TicketHistory>(builder, "TicketHistories");
        history.Property(x => x.EventType).HasConversion<string>().HasMaxLength(64);
        history.Property(x => x.ActorNameSnapshot).HasMaxLength(200).IsRequired();
        history.Property(x => x.OldPriority).HasConversion<byte>();
        history.Property(x => x.NewPriority).HasConversion<byte>();
        history.HasIndex(x => new { x.TicketId, x.CreatedAtUtc, x.Id });
        history.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId);
        history.HasOne<Delegation>().WithMany().HasForeignKey(x => x.DelegationId);
        history.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId);
        history.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OldAssigneeUserId);
        history.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.NewAssigneeUserId);
        history.ToTable("TicketHistories", t =>
        {
            t.HasCheckConstraint("CK_TicketHistories_Event",
                "[EventType] IN ('Assigned','Reassigned','PriorityChanged','Delegated','DelegationReturned')");
            t.HasCheckConstraint("CK_TicketHistories_OldPriority", "[OldPriority] IS NULL OR [OldPriority] BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_TicketHistories_NewPriority", "[NewPriority] IS NULL OR [NewPriority] BETWEEN 1 AND 4");
        });
    }
}
