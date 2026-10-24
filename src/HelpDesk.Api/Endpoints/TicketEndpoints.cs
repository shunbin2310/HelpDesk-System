using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Constants;

namespace HelpDesk.Api.Endpoints;

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this IEndpointRouteBuilder routes)
    {
        var tickets = routes.MapGroup("/tickets").RequireAuthorization();
        tickets.MapGet("/", async (int? page, string? status, bool? onlyUnassigned, ITicketService service, CancellationToken ct) =>
            ToHttp(await service.ListAsync(page ?? 1, status, onlyUnassigned ?? false, ct)));
        tickets.MapGet("/{ticketId:long}", async (long ticketId, ITicketService service, CancellationToken ct) => ToHttp(await service.GetAsync(ticketId, ct)));
        tickets.MapPost("/", async (CreateTicketRequest request, ITicketService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.Status == TicketResultStatus.Success ? Results.Created($"/tickets/{result.Value!.Ticket.Id}", result.Value) : ToHttp(result);
        }).RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Employee));
        tickets.MapGet("/assignees", async (ITicketService service, CancellationToken ct) => ToHttp(await service.GetAssigneesAsync(ct)))
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.SystemAdmin, ApplicationRoles.SupportAdmin));
        tickets.MapPut("/{ticketId:long}/assignment", async (long ticketId, AssignTicketRequest request, ITicketService service, CancellationToken ct) =>
            ToHttp(await service.AssignAsync(ticketId, request, ct))).RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.SystemAdmin, ApplicationRoles.SupportAdmin));
        tickets.MapPut("/{ticketId:long}/priority", async (long ticketId, SetTicketPriorityRequest request, ITicketService service, CancellationToken ct) =>
            ToHttp(await service.SetPriorityAsync(ticketId, request, ct))).RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.SystemAdmin, ApplicationRoles.SupportAdmin));
    }

    private static IResult ToHttp<T>(TicketResult<T> result) => result.Status switch
    {
        TicketResultStatus.Success => Results.Ok(result.Value),
        TicketResultStatus.NotFound => Results.Problem(statusCode: 404, title: "Ticket not found", detail: result.Error),
        TicketResultStatus.Forbidden => Results.Problem(statusCode: 403, title: "Ticket access denied", detail: result.Error),
        TicketResultStatus.Conflict => Results.Problem(statusCode: 409, title: "Ticket could not be saved", detail: result.Error),
        _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["ticket"] = [result.Error ?? "Invalid Ticket request."] })
    };
}
