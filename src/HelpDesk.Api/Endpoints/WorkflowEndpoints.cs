using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Constants;

namespace HelpDesk.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder routes)
    {
        var templates = routes.MapGroup("/workflow-templates").RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.SystemAdmin));
        templates.MapGet("/active", async (IWorkflowTemplateService service, CancellationToken ct) => ToHttp(await service.GetActiveAsync(ct)));
        templates.MapPost("/default", async (IWorkflowTemplateService service, CancellationToken ct) => ToHttp(await service.InitializeDefaultAsync(ct)));
        var workflow = routes.MapGroup("/tickets/{ticketId:long}/workflow").RequireAuthorization();
        workflow.MapGet("/", async (long ticketId, IWorkflowEngine engine, CancellationToken ct) => ToHttp(await engine.GetAsync(ticketId, ct)));
        workflow.MapPost("/actions", async (long ticketId, ExecuteWorkflowAction request, IWorkflowEngine engine, CancellationToken ct) =>
            ToHttp(await engine.ExecuteAsync(ticketId, request, ct)));
    }

    private static IResult ToHttp<T>(WorkflowResult<T> result) => result.Status switch
    {
        WorkflowResultStatus.Success => Results.Ok(result.Value),
        WorkflowResultStatus.NotFound => Results.Problem(statusCode: 404, title: "Workflow not found", detail: result.Error),
        WorkflowResultStatus.Forbidden => Results.Problem(statusCode: 403, title: "Workflow access denied", detail: result.Error),
        WorkflowResultStatus.Conflict => Results.Problem(statusCode: 409, title: "Workflow changed", detail: result.Error),
        _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["workflow"] = [result.Error ?? "Invalid workflow request."] })
    };
}
