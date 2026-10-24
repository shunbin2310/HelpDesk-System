using HelpDesk.Application.Users;
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
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<HelpDeskDbContext>();
        services.AddScoped<IUserAccountStore, UserAccountStore>();
        services.Configure<DatabaseMigrationOptions>(configuration.GetSection(DatabaseMigrationOptions.SectionName));
        services.AddScoped<IStartupMigrationDatabase, EfStartupMigrationDatabase>();
        services.AddScoped<StartupDatabaseMigrator>();
        return services;
    }
}
