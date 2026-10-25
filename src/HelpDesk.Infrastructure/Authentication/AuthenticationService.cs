using HelpDesk.Application.Authentication;
using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.Identity;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Authentication;

public sealed class AuthenticationService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    HelpDeskDbContext db, JwtTokenService tokens) : IAuthenticationService
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 256
            || string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            return null;
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
            return null;
        var check = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
            return null;
        var account = await GetUserAsync(user.Id, cancellationToken);
        return account is null ? null : tokens.Create(account, user.SecurityStamp!);
    }

    public async Task<UserAccount?> GetUserAsync(int userId, CancellationToken cancellationToken = default) =>
        await (from user in db.Users.AsNoTracking()
               join membership in db.UserRoles on user.Id equals membership.UserId
               join role in db.Roles on membership.RoleId equals role.Id
               where user.Id == userId && user.IsActive
               select new UserAccount(user.Id, user.Email!, user.FullName, role.Name!, user.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<UserAccountResult> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 128
            || string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length > 128)
            return new(null, ["Passwords are required and must be at most 128 characters."]);
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (user is null || !user.IsActive)
            return new(null, ["User is unavailable."]);
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded ? new(userId, []) : new(null, result.Errors.Select(x => x.Description).ToArray());
    }
}
