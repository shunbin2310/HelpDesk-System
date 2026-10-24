using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;

namespace HelpDesk.Api.Endpoints;

public sealed record SetRoleRequest(string Role);
public sealed record SetActiveRequest(bool? IsActive);
public sealed record ResetPasswordRequest(string Password);

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/users").RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.SystemAdmin));
        group.MapGet("/", (int? page, IUserAccountStore users, CancellationToken ct) => users.ListAsync(page ?? 1, ct));
        group.MapPost("/", async (CreateUserAccount request, IUserAccountStore users, CancellationToken ct) =>
            AuthenticationEndpoints.ToResult(await users.CreateAsync(request, ct)));
        group.MapPut("/{id:int}/role", async (int id, SetRoleRequest request, IUserAccountStore users, CancellationToken ct) =>
            AuthenticationEndpoints.ToResult(await users.SetRoleAsync(id, request.Role, ct)));
        group.MapPut("/{id:int}/active", async (int id, SetActiveRequest request, IUserAccountStore users, CancellationToken ct) =>
            request.IsActive.HasValue ? AuthenticationEndpoints.ToResult(await users.SetActiveAsync(id, request.IsActive.Value, ct))
                : Results.ValidationProblem(new Dictionary<string, string[]> { ["isActive"] = ["Choose whether the account is active."] }));
        group.MapPost("/{id:int}/reset-password", async (int id, ResetPasswordRequest request, IUserAccountStore users, CancellationToken ct) =>
            AuthenticationEndpoints.ToResult(await users.ResetPasswordAsync(id, request.Password, ct)));
    }
}
