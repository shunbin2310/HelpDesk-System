using HelpDesk.Domain.Common;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Entities.Delegations;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistence;

public sealed class HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<TicketHistory> TicketHistories => Set<TicketHistory>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<DelegationTicket> DelegationTickets => Set<DelegationTicket>();
    public DbSet<WorkflowProcessTemplate> WorkflowProcessTemplates => Set<WorkflowProcessTemplate>();
    public DbSet<WorkflowStepTemplate> WorkflowStepTemplates => Set<WorkflowStepTemplate>();
    public DbSet<WorkflowActionTemplate> WorkflowActionTemplates => Set<WorkflowActionTemplate>();
    public DbSet<WorkflowActionerTemplate> WorkflowActionerTemplates => Set<WorkflowActionerTemplate>();
    public DbSet<WorkflowProcessInstance> WorkflowProcessInstances => Set<WorkflowProcessInstance>();
    public DbSet<WorkflowStepInstance> WorkflowStepInstances => Set<WorkflowStepInstance>();
    public DbSet<WorkflowActionInstance> WorkflowActionInstances => Set<WorkflowActionInstance>();
    public DbSet<WorkflowActionerInstance> WorkflowActionerInstances => Set<WorkflowActionerInstance>();
    public DbSet<WorkflowHistory> WorkflowHistory => Set<WorkflowHistory>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetCurrentStepOwners();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetCurrentStepOwners();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SetCurrentStepOwners()
    {
        // The optional current-step FK must not share the process's own primary key:
        // EF may null optional FK components while inserting the cyclic relationship.
        foreach (var entry in ChangeTracker.Entries<WorkflowProcessInstance>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property<long?>("CurrentStepProcessInstanceId").CurrentValue =
                entry.Entity.CurrentStepInstanceId.HasValue ? entry.Entity.Id : null;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        IdentityMappings.Configure(builder);
        TicketMappings.Configure(builder);
        WorkflowMappings.Configure(builder);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entity.GetForeignKeys())
                foreignKey.DeleteBehavior = DeleteBehavior.NoAction;

            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) ||
                    property.ClrType == typeof(DateTimeOffset?))
                    property.SetColumnType("datetimeoffset(7)");
            }

            if (typeof(VersionedEntity).IsAssignableFrom(entity.ClrType))
                builder.Entity(entity.ClrType).Property<byte[]>("RowVersion").IsRowVersion();
        }
    }
}
