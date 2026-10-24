using HelpDesk.Application.Users;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Workflows;

internal static class WorkflowActorReader
{
    public static Task<UserAccount?> ReadAsync(HelpDeskDbContext db, int userId, CancellationToken ct) =>
        (from user in db.Users.AsNoTracking()
         join membership in db.UserRoles on user.Id equals membership.UserId
         join role in db.Roles on membership.RoleId equals role.Id
         where user.Id == userId && user.IsActive
         select new UserAccount(user.Id, user.Email!, user.FullName, role.Name!, user.IsActive)).SingleOrDefaultAsync(ct);
}
