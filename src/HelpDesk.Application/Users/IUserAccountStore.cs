namespace HelpDesk.Application.Users;

public sealed record CreateUserAccount(string Email, string FullName, string Password, string Role);

public sealed record UserAccountResult(int? UserId, IReadOnlyList<string> Errors)
{
    public bool Succeeded => UserId.HasValue && Errors.Count == 0;
}

public interface IUserAccountStore
{
    Task<UserAccountResult> CreateAsync(CreateUserAccount account, CancellationToken cancellationToken = default);
    Task<UserAccountResult> SetRoleAsync(int userId, string role, CancellationToken cancellationToken = default);
}
