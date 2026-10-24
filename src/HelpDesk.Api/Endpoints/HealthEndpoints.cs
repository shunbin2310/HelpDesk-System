namespace HelpDesk.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => TypedResults.Ok(new
        {
            status = "healthy",
            service = "HelpDesk.Api",
            timestamp = DateTimeOffset.UtcNow
        }));

        return endpoints;
    }
}
