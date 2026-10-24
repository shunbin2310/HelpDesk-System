namespace HelpDesk.Application.Users;

public sealed record CreateUserAccount(string Email, string FullName, string Password, string Role);
public sealed record UserAccount(int Id, string Email, string FullName, string Role, bool IsActive);
public sealed record UserAccountPage(IReadOnlyList<UserAccount> Items, int Total, int Page);

public sealed record UserAccountResult(int? UserId, IReadOnlyList<string> Errors)
{
    public bool Succeeded => UserId.HasValue && Errors.Count == 0;
}

public interface IUserAccountStore
{
    Task<UserAccountResult> CreateAsync(CreateUserAccount account, CancellationToken cancellationToken = default);
    Task<UserAccountResult> SetRoleAsync(int userId, string role, CancellationToken cancellationToken = default);
    Task<UserAccountPage> ListAsync(int page, CancellationToken cancellationToken = default);
    Task<UserAccountResult> SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken = default);
    Task<UserAccountResult> ResetPasswordAsync(int userId, string password, CancellationToken cancellationToken = default);
    Task<UserAccountResult> BootstrapAdminAsync(CreateUserAccount account, CancellationToken cancellationToken = default);
}
