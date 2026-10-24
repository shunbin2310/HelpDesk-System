using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Identity;

public sealed class UserAccountStore(HelpDeskDbContext context, UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<int>> roles) : IUserAccountStore
{
    public async Task<UserAccountPage> ListAsync(int page, CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, 1000000);
        var query = from user in context.Users.AsNoTracking()
                    join membership in context.UserRoles on user.Id equals membership.UserId
                    join role in context.Roles on membership.RoleId equals role.Id
                    select new { user.Id, user.Email, user.FullName, Role = role.Name, user.IsActive };
        return new(await query.OrderBy(x => x.Id).Skip((page - 1) * 25).Take(25)
                .Select(x => new UserAccount(x.Id, x.Email!, x.FullName, x.Role!, x.IsActive)).ToListAsync(cancellationToken),
            await query.CountAsync(cancellationToken), page);
    }

    public Task<UserAccountResult> CreateAsync(CreateUserAccount account, CancellationToken cancellationToken = default) =>
        MutateAsync(() => CreateCoreAsync(account), cancellationToken);

    public Task<UserAccountResult> BootstrapAdminAsync(CreateUserAccount account, CancellationToken cancellationToken = default) =>
        MutateAsync(async () =>
        {
            if (await Admins().AnyAsync(cancellationToken)) return Failed("A System Admin already exists. Use user management.");
            return await CreateCoreAsync(account with { Role = ApplicationRoles.SystemAdmin });
        }, cancellationToken);

    private async Task<UserAccountResult> CreateCoreAsync(CreateUserAccount account)
    {
        if (!ApplicationRoles.IsValid(account.Role) || !await roles.RoleExistsAsync(account.Role)) return Failed("Unknown role.");
        if (string.IsNullOrWhiteSpace(account.Email) || account.Email.Length > 256
            || string.IsNullOrWhiteSpace(account.FullName) || account.FullName.Length > 200)
            return Failed("Email (at most 256 characters) and full name (at most 200 characters) are required.");
        if (string.IsNullOrEmpty(account.Password) || account.Password.Length > 128)
            return Failed("Password is required and must be at most 128 characters.");
        var user = new ApplicationUser
        {
            Email = account.Email.Trim(), UserName = account.Email.Trim(), FullName = account.FullName.Trim()
        };
        var result = await users.CreateAsync(user, account.Password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, account.Role);
        return Result(user.Id, result);
    }

    public Task<UserAccountResult> SetRoleAsync(int userId, string role, CancellationToken cancellationToken = default) =>
        MutateAsync(async () =>
        {
            if (!ApplicationRoles.IsValid(role) || !await roles.RoleExistsAsync(role)) return Failed("Unknown role.");
            var user = await FindAsync(userId);
            if (user is null) return Failed("User not found.");
            var previous = await users.GetRolesAsync(user);
            if (previous.Count == 1 && previous[0] == role) return new(user.Id, []);
            if (role != ApplicationRoles.SystemAdmin && await IsLastActiveAdminAsync(user, cancellationToken))
                return Failed("Keep at least one active System Admin.");
            var result = await users.RemoveFromRolesAsync(user, previous);
            if (result.Succeeded) result = await users.AddToRoleAsync(user, role);
            if (result.Succeeded)
            {
                user.UpdatedAtUtc = DateTimeOffset.UtcNow;
                result = await users.UpdateSecurityStampAsync(user);
            }
            return Result(user.Id, result);
        }, cancellationToken);

    public Task<UserAccountResult> SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken = default) =>
        MutateAsync(async () =>
        {
            var user = await FindAsync(userId);
            if (user is null) return Failed("User not found.");
            if (user.IsActive == isActive) return new(user.Id, []);
            if (!isActive && await IsLastActiveAdminAsync(user, cancellationToken))
                return Failed("Keep at least one active System Admin.");
            user.IsActive = isActive;
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return Result(user.Id, await users.UpdateSecurityStampAsync(user));
        }, cancellationToken);

    public Task<UserAccountResult> ResetPasswordAsync(int userId, string password, CancellationToken cancellationToken = default) =>
        MutateAsync(async () =>
        {
            if (string.IsNullOrEmpty(password) || password.Length > 128)
                return Failed("Password is required and must be at most 128 characters.");
            var user = await FindAsync(userId);
            if (user is null) return Failed("User not found.");
            var token = await users.GeneratePasswordResetTokenAsync(user);
            return Result(user.Id, await users.ResetPasswordAsync(user, token, password));
        }, cancellationToken);

    private Task<ApplicationUser?> FindAsync(int userId) =>
        users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private IQueryable<ApplicationUser> Admins() =>
        from user in context.Users
        join membership in context.UserRoles on user.Id equals membership.UserId
        join role in context.Roles on membership.RoleId equals role.Id
        where role.Name == ApplicationRoles.SystemAdmin
        select user;

    private async Task<bool> IsLastActiveAdminAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        user.IsActive && await users.IsInRoleAsync(user, ApplicationRoles.SystemAdmin)
        && await Admins().CountAsync(x => x.IsActive, cancellationToken) <= 1;

    private async Task<UserAccountResult> MutateAsync(Func<Task<UserAccountResult>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize administration across API processes to protect bootstrap and the last active admin.
            await context.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=N'HelpDesk.UserAdministration',
                    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51000, 'User administration is busy. Try again.', 1;
                """, cancellationToken);
            var result = await operation();
            if (result.Succeeded) await transaction.CommitAsync(cancellationToken);
            else
            {
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            return Failed("This account changed. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            return Failed("This email or role assignment already exists.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static UserAccountResult Failed(string error) => new(null, [error]);
    private static UserAccountResult Result(int userId, IdentityResult result) =>
        result.Succeeded ? new(userId, []) : new(null, result.Errors.Select(x => x.Description).ToArray());
}
