using HelpDesk.Application.Users;
using HelpDesk.Application.Delegations;
using HelpDesk.Infrastructure.Delegations;
using HelpDesk.Application.Workflows;
using HelpDesk.Application.Tickets;
using HelpDesk.Infrastructure.Tickets;
using HelpDesk.Infrastructure.Attachments;
using HelpDesk.Infrastructure.Workflows;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Infrastructure.Persistence.StartupMigration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        services.AddDbContext<HelpDeskDbContext>(options => options.UseSqlServer(connection));
        services.AddAuthentication();
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<HelpDeskDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddDataProtection();
        services.AddScoped<IUserAccountStore, UserAccountStore>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDelegationService, DelegationService>();
        services.AddScoped<DelegationProcessor>();
        services.Configure<DelegationProcessingOptions>(configuration.GetSection("DelegationProcessing"));
        services.AddHostedService<DelegationWorker>();
        services.AddScoped<ITicketAttachmentService, TicketAttachmentService>();
        services.AddOptions<AttachmentStorageOptions>().Bind(configuration.GetSection("Attachments"))
            .Validate(x => !string.IsNullOrWhiteSpace(x.RootPath), "Attachments:RootPath must specify a private directory.").ValidateOnStart();
        services.AddSingleton<IAttachmentStore, FileAttachmentStore>();
        services.AddScoped<TicketAttachmentWriter>();
        services.AddHostedService<AttachmentReconciler>();
        services.AddScoped<IWorkflowTemplateService, WorkflowTemplateService>();
        services.Configure<DatabaseMigrationOptions>(configuration.GetSection(DatabaseMigrationOptions.SectionName));
        services.AddScoped<IStartupMigrationDatabase, EfStartupMigrationDatabase>();
        services.AddScoped<StartupDatabaseMigrator>();
        return services;
    }
}
