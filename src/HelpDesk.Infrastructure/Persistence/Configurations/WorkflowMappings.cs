using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistence.Configurations;

internal static class WorkflowMappings
{
    private const string Steps = "'OPEN','IN_PROGRESS','RESOLVED','CLOSED'";
    private const string Roles = "'EMPLOYEE','SUPPORT_IT_STAFF','SUPPORT_ADMIN','SYSTEM_ADMIN'";

    public static void Configure(ModelBuilder builder)
    {
        var process = TicketMappings.Entity<WorkflowProcessTemplate>(builder, "WorkflowProcessTemplates");
        process.Property(x => x.Code).HasMaxLength(64).IsRequired();
        process.Property(x => x.Name).HasMaxLength(200).IsRequired();
        process.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
        process.HasIndex(x => new { x.Code, x.Version }).IsUnique();
        process.HasIndex(x => x.EntityType).IsUnique().HasFilter("[IsPublished] = 1 AND [IsActive] = 1");
        process.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId);
        process.ToTable("WorkflowProcessTemplates", t =>
        {
            t.HasCheckConstraint("CK_WorkflowTemplates_Version", "[Version] > 0");
            t.HasCheckConstraint("CK_WorkflowTemplates_Active", "[IsActive] = 0 OR [IsPublished] = 1");
        });

        var step = TicketMappings.Entity<WorkflowStepTemplate>(builder, "WorkflowStepTemplates");
        step.Property(x => x.Code).HasMaxLength(64).IsRequired();
        step.Property(x => x.Name).HasMaxLength(200).IsRequired();
        step.HasAlternateKey(x => new { x.ProcessTemplateId, x.Id });
        step.HasIndex(x => new { x.ProcessTemplateId, x.Code }).IsUnique();
        step.HasIndex(x => x.ProcessTemplateId).IsUnique().HasFilter("[IsInitial] = 1");
        step.HasOne<WorkflowProcessTemplate>().WithMany().HasForeignKey(x => x.ProcessTemplateId);
        step.ToTable("WorkflowStepTemplates", t => t.HasCheckConstraint("CK_WorkflowSteps_Template", StepCheck()));

        var action = TicketMappings.Entity<WorkflowActionTemplate>(builder, "WorkflowActionTemplates");
        action.Property(x => x.Code).HasMaxLength(64).IsRequired();
        action.Property(x => x.Name).HasMaxLength(200).IsRequired();
        action.HasIndex(x => new { x.FromStepTemplateId, x.Code }).IsUnique();
        action.HasOne<WorkflowStepTemplate>().WithMany()
            .HasForeignKey(x => new { x.ProcessTemplateId, x.FromStepTemplateId })
            .HasPrincipalKey(x => new { x.ProcessTemplateId, x.Id });
        action.HasOne<WorkflowStepTemplate>().WithMany()
            .HasForeignKey(x => new { x.ProcessTemplateId, x.ToStepTemplateId })
            .HasPrincipalKey(x => new { x.ProcessTemplateId, x.Id });
        action.ToTable("WorkflowActionTemplates", t => t.HasCheckConstraint("CK_WorkflowActions_Template",
            "[FromStepTemplateId] <> [ToStepTemplateId]"));

        var actioner = TicketMappings.Entity<WorkflowActionerTemplate>(builder, "WorkflowActionerTemplates");
        actioner.Property(x => x.ActionerType).HasConversion<byte>();
        actioner.Property(x => x.ActionerRole).HasMaxLength(64);
        actioner.HasOne<WorkflowActionTemplate>().WithMany().HasForeignKey(x => x.ActionTemplateId);
        actioner.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActionerUserId);
        actioner.ToTable("WorkflowActionerTemplates", t => t.HasCheckConstraint("CK_Actioners_Template", ActionerCheck(false)));

        var instance = TicketMappings.Entity<WorkflowProcessInstance>(builder, "WorkflowProcessInstances");
        // Allocate a stable key before the process and its current-step owner are saved.
        instance.Property(x => x.Id).UseHiLo("WorkflowProcessInstanceIds");
        instance.Property<long?>("CurrentStepProcessInstanceId");
        instance.Property(x => x.TemplateCode).HasMaxLength(64).IsRequired();
        instance.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
        instance.Property(x => x.Status).HasConversion<byte>();
        instance.HasIndex(x => x.TicketId).IsUnique();
        instance.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId);
        instance.HasOne<WorkflowProcessTemplate>().WithMany().HasForeignKey(x => x.ProcessTemplateId);
        instance.HasOne<WorkflowStepInstance>().WithMany()
            .HasForeignKey("CurrentStepProcessInstanceId", nameof(WorkflowProcessInstance.CurrentStepInstanceId))
            .HasPrincipalKey(x => new { x.ProcessInstanceId, x.Id });
        instance.ToTable("WorkflowProcessInstances", t =>
        {
            t.HasCheckConstraint("CK_WorkflowInstances_Status",
                "([Status] = 1 AND [CompletedAtUtc] IS NULL) OR ([Status] = 2 AND [CompletedAtUtc] IS NOT NULL)");
            t.HasCheckConstraint("CK_WorkflowInstances_CurrentStepOwner",
                "([CurrentStepInstanceId] IS NULL AND [CurrentStepProcessInstanceId] IS NULL) OR " +
                "([CurrentStepInstanceId] IS NOT NULL AND [CurrentStepProcessInstanceId] IS NOT NULL AND [CurrentStepProcessInstanceId] = [Id])");
        });

        var stepInstance = TicketMappings.Entity<WorkflowStepInstance>(builder, "WorkflowStepInstances");
        stepInstance.Property(x => x.Code).HasMaxLength(64).IsRequired();
        stepInstance.Property(x => x.Name).HasMaxLength(200).IsRequired();
        stepInstance.HasAlternateKey(x => new { x.ProcessInstanceId, x.Id });
        stepInstance.HasIndex(x => new { x.ProcessInstanceId, x.Code }).IsUnique();
        stepInstance.HasIndex(x => x.ProcessInstanceId).IsUnique().HasFilter("[IsInitial] = 1");
        stepInstance.HasOne<WorkflowProcessInstance>().WithMany().HasForeignKey(x => x.ProcessInstanceId);
        stepInstance.HasOne<WorkflowStepTemplate>().WithMany().HasForeignKey(x => x.SourceStepTemplateId);
        stepInstance.ToTable("WorkflowStepInstances", t => t.HasCheckConstraint("CK_WorkflowSteps_Instance", StepCheck()));

        var actionInstance = TicketMappings.Entity<WorkflowActionInstance>(builder, "WorkflowActionInstances");
        actionInstance.Property(x => x.Code).HasMaxLength(64).IsRequired();
        actionInstance.Property(x => x.Name).HasMaxLength(200).IsRequired();
        actionInstance.HasAlternateKey(x => new { x.ProcessInstanceId, x.Id });
        actionInstance.HasIndex(x => new { x.FromStepInstanceId, x.Code }).IsUnique();
        actionInstance.HasOne<WorkflowStepInstance>().WithMany()
            .HasForeignKey(x => new { x.ProcessInstanceId, x.FromStepInstanceId })
            .HasPrincipalKey(x => new { x.ProcessInstanceId, x.Id });
        actionInstance.HasOne<WorkflowStepInstance>().WithMany()
            .HasForeignKey(x => new { x.ProcessInstanceId, x.ToStepInstanceId })
            .HasPrincipalKey(x => new { x.ProcessInstanceId, x.Id });
        actionInstance.HasOne<WorkflowActionTemplate>().WithMany().HasForeignKey(x => x.SourceActionTemplateId);
        actionInstance.ToTable("WorkflowActionInstances", t => t.HasCheckConstraint("CK_WorkflowActions_Instance",
            "[FromStepInstanceId] <> [ToStepInstanceId]"));

        var actionerInstance = TicketMappings.Entity<WorkflowActionerInstance>(builder, "WorkflowActionerInstances");
        actionerInstance.Property(x => x.ActionerType).HasConversion<byte>();
        actionerInstance.Property(x => x.ActionerRole).HasMaxLength(64);
        actionerInstance.HasOne<WorkflowActionInstance>().WithMany().HasForeignKey(x => x.ActionInstanceId);
        actionerInstance.HasOne<WorkflowActionerTemplate>().WithMany().HasForeignKey(x => x.SourceActionerTemplateId);
        actionerInstance.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActionerUserId);
        actionerInstance.ToTable("WorkflowActionerInstances", t => t.HasCheckConstraint("CK_Actioners_Instance", ActionerCheck(true)));

        var history = TicketMappings.Entity<WorkflowHistory>(builder, "WorkflowHistory");
        history.Property(x => x.FromStepCode).HasMaxLength(64);
        history.Property(x => x.ToStepCode).HasMaxLength(64).IsRequired();
        history.Property(x => x.ActionCode).HasMaxLength(64).IsRequired();
        history.Property(x => x.ActionByNameSnapshot).HasMaxLength(200).IsRequired();
        history.HasIndex(x => new { x.ProcessInstanceId, x.ActionAtUtc, x.Id });
        history.HasOne<WorkflowProcessInstance>().WithMany().HasForeignKey(x => x.ProcessInstanceId);
        history.HasOne<WorkflowActionInstance>().WithMany()
            .HasForeignKey(x => new { x.ProcessInstanceId, x.ActionInstanceId })
            .HasPrincipalKey(x => new { x.ProcessInstanceId, x.Id });
        history.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActionByUserId);
    }

    private static string StepCheck() =>
        $"[Code] IN ({Steps}) AND (([Code] = 'OPEN' AND [IsInitial] = 1) OR ([Code] <> 'OPEN' AND [IsInitial] = 0)) " +
        "AND (([Code] = 'CLOSED' AND [IsTerminal] = 1) OR ([Code] <> 'CLOSED' AND [IsTerminal] = 0))";

    private static string ActionerCheck(bool instance)
    {
        var requester = instance ? "[ActionerUserId] IS NOT NULL" : "[ActionerUserId] IS NULL";
        return $"([ActionerType] = 1 AND [ActionerRole] IS NULL AND {requester}) OR " +
            "([ActionerType] = 2 AND [ActionerRole] IS NULL AND [ActionerUserId] IS NULL) OR " +
            $"([ActionerType] = 3 AND [ActionerRole] IS NOT NULL AND [ActionerRole] IN ({Roles}) AND [ActionerUserId] IS NULL) OR " +
            "([ActionerType] = 4 AND [ActionerRole] IS NULL AND [ActionerUserId] IS NOT NULL)";
    }
}
