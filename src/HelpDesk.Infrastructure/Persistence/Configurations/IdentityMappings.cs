using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistence.Configurations;

internal static class IdentityMappings
{
    public static void Configure(ModelBuilder builder)
    {
        var user = builder.Entity<ApplicationUser>();
        user.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        user.Property(x => x.Email).HasMaxLength(256).IsRequired();
        user.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        user.Property(x => x.UserName).HasMaxLength(256).IsRequired();
        user.Property(x => x.NormalizedUserName).HasMaxLength(256).IsRequired();
        user.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();

        // Identity's composite PK alone permits several roles per user.
        builder.Entity<IdentityUserRole<int>>().HasIndex(x => x.UserId).IsUnique();
        var role = builder.Entity<IdentityRole<int>>();
        role.Property(x => x.Name).HasMaxLength(64).IsRequired();
        role.Property(x => x.NormalizedName).HasMaxLength(64).IsRequired();
        role.ToTable("AspNetRoles", t => t.HasCheckConstraint("CK_Roles_Name",
            "[Name] IN ('EMPLOYEE','SUPPORT_IT_STAFF','SUPPORT_ADMIN','SYSTEM_ADMIN')"));

        // Stable seed IDs are storage details; business code resolves roles by name.
        role.HasData(ApplicationRoles.All.Select((name, index) => new IdentityRole<int>
        {
            Id = index + 1, Name = name, NormalizedName = name,
            ConcurrencyStamp = $"helpdesk-role-{name}"
        }));
    }
}
