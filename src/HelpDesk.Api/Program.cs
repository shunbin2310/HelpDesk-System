using HelpDesk.Api.Endpoints;
using HelpDesk.Infrastructure.DependencyInjection;
using HelpDesk.Infrastructure.Persistence.StartupMigration;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<StartupDatabaseMigrator>()
        .ApplyAsync(app.Lifetime.ApplicationStopping);
}

app.MapHealthEndpoints();

app.Run();
