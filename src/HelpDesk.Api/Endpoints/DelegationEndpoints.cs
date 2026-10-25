using HelpDesk.Application.Delegations;
using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Constants;

namespace HelpDesk.Api.Endpoints;

public static class DelegationEndpoints
{
    public static void MapDelegationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/delegations").RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.SystemAdmin, ApplicationRoles.SupportAdmin));
        group.MapGet("/", async (int? page, IDelegationService service, CancellationToken ct) => ToHttp(await service.ListAsync(page ?? 1, ct)));
        group.MapGet("/{id:long}", async (long id, int? page, IDelegationService service, CancellationToken ct) => ToHttp(await service.GetAsync(id, page ?? 1, ct)));
        group.MapPost("/{id:long}/cancel", async (long id, StopDelegation request, IDelegationService service, CancellationToken ct) => ToHttp(await service.CancelAsync(id, request, ct)));
        group.MapPost("/{id:long}/end-early", async (long id, StopDelegation request, IDelegationService service, CancellationToken ct) => ToHttp(await service.EndEarlyAsync(id, request, ct)));
        group.MapPost("/", async (CreateDelegation request, IDelegationService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.Status == TicketResultStatus.Success ? Results.Created($"/delegations/{result.Value!.Id}", result.Value) : ToHttp(result);
        });
    }

    private static IResult ToHttp<T>(TicketResult<T> result) => result.Status switch
    {
        TicketResultStatus.Success => Results.Ok(result.Value),
        TicketResultStatus.Forbidden => Results.Problem(statusCode: 403, title: "Delegation access denied", detail: result.Error),
        TicketResultStatus.NotFound => Results.Problem(statusCode: 404, title: "Delegation not found", detail: result.Error),
        TicketResultStatus.Conflict => Results.Problem(statusCode: 409, title: "Delegation conflict", detail: result.Error),
        _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["delegation"] = [result.Error ?? "Invalid delegation request."] })
    };
}
