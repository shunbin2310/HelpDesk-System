using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Identity;

// No HTTP endpoints are exposed in stage 1. Future user-management use cases authorize callers.
public sealed class UserAccountStore(
    HelpDeskDbContext context,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<int>> roles) : IUserAccountStore
{
    public async Task<UserAccountResult> CreateAsync(CreateUserAccount account, CancellationToken cancellationToken = default)
    {
        if (!ApplicationRoles.IsValid(account.Role) || !await roles.RoleExistsAsync(account.Role))
            return Failed("Unknown role.");
        if (string.IsNullOrWhiteSpace(account.Email) || string.IsNullOrWhiteSpace(account.FullName))
            return Failed("Email and full name are required.");

        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new ApplicationUser
            {
                Email = account.Email.Trim(), UserName = account.Email.Trim(),
                FullName = account.FullName.Trim()
            };
            var created = await users.CreateAsync(user, account.Password);
            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return Failed(created);
            }
            var membership = await users.AddToRoleAsync(user, account.Role);
            if (!membership.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return Failed(membership);
            }
            await transaction.CommitAsync(cancellationToken);
            return new(user.Id, []);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<UserAccountResult> SetRoleAsync(int userId, string role, CancellationToken cancellationToken = default)
    {
        if (!ApplicationRoles.IsValid(role) || !await roles.RoleExistsAsync(role))
            return Failed("Unknown role.");

        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (user is null) return Failed("User not found.");

            var previous = await users.GetRolesAsync(user);
            if (previous.Count == 1 && previous[0] == role)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(user.Id, []);
            }
            var removed = await users.RemoveFromRolesAsync(user, previous);
            var added = removed.Succeeded ? await users.AddToRoleAsync(user, role) : removed;
            var stamp = added.Succeeded ? await users.UpdateSecurityStampAsync(user) : added;
            if (!stamp.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return Failed(stamp);
            }
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(user.Id, []);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static UserAccountResult Failed(string error) => new(null, [error]);
    private static UserAccountResult Failed(IdentityResult result) =>
        new(null, result.Errors.Select(x => x.Description).ToArray());
}
