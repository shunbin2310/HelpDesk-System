using HelpDesk.Api.Endpoints;
using HelpDesk.Api.Authentication;
using HelpDesk.Application.Authentication;
using HelpDesk.Infrastructure.DependencyInjection;
using HelpDesk.Infrastructure.Persistence.StartupMigration;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var bootstrap = args.FirstOrDefault() == "bootstrap-admin";
var builder = WebApplication.CreateBuilder(bootstrap ? args[1..] : args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
if (!bootstrap)
{
    builder.Services.AddHelpDeskAuthentication(builder.Configuration);
    builder.Services.AddRateLimiter(options =>
    {
        // shortcut: limits are per API process; use shared limits before deploying multiple API replicas.
        options.RejectionStatusCode = 429;
        options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
    });
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<StartupDatabaseMigrator>()
        .ApplyAsync(app.Lifetime.ApplicationStopping);
}

app.MapHealthEndpoints();
if (bootstrap)
{
    Environment.ExitCode = await BootstrapAdminCommand.RunAsync(app.Services, app.Environment);
    return;
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapAuthenticationEndpoints();
app.MapUserEndpoints();
app.MapWorkflowEndpoints();
app.MapTicketEndpoints();

app.Run();

public partial class Program;
