using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HelpDesk.Infrastructure.Persistence;

// Tooling creates only the context, never starts HTTP, background jobs or startup migrations.
public sealed class HelpDeskDbContextFactory : IDesignTimeDbContextFactory<HelpDeskDbContext>
{
    public const string DevelopmentConnection =
        "Server=BIN;Database=HelpDeskDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

    public HelpDeskDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? DevelopmentConnection;
        return new HelpDeskDbContext(new DbContextOptionsBuilder<HelpDeskDbContext>()
            .UseSqlServer(connection).Options);
    }
}
