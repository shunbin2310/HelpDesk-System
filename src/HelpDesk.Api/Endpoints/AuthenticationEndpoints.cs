using HelpDesk.Application.Authentication;
using HelpDesk.Application.Users;

namespace HelpDesk.Api.Endpoints;

public static class AuthenticationEndpoints
{
    public static void MapAuthenticationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth");
        group.MapPost("/login", async (LoginRequest request, IAuthenticationService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request, ct);
            return result is null ? Results.Problem(statusCode: 401, title: "Unable to sign in.",
                detail: "Check your email and password, or contact your System Admin if your account is unavailable or locked.")
                : Results.Ok(result);
        }).AllowAnonymous().RequireRateLimiting("login");

        group.MapGet("/me", async (ICurrentUserContext current, IAuthenticationService auth, CancellationToken ct) =>
        {
            var user = await auth.GetUserAsync(current.UserId, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        }).RequireAuthorization();

        group.MapPost("/change-password", async (ChangePasswordRequest request, ICurrentUserContext current,
            IAuthenticationService auth, CancellationToken ct) => ToResult(await auth.ChangePasswordAsync(current.UserId, request, ct)))
            .RequireAuthorization().RequireRateLimiting("login");
    }

    internal static IResult ToResult(UserAccountResult result) => result.Succeeded ? Results.Ok(new { result.UserId })
        : Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = result.Errors.ToArray() });
}
