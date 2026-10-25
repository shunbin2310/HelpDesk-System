using HelpDesk.Application.Users;

namespace HelpDesk.Application.Authentication;

public sealed record LoginRequest(string Email, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserAccount User);

public interface IAuthenticationService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<UserAccount?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserAccountResult> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

public interface ICurrentUserContext
{
    int UserId
    {
        get;
    }
}
