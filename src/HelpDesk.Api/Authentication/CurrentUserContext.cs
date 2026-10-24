using System.Security.Claims;
using HelpDesk.Application.Authentication;

namespace HelpDesk.Api.Authentication;

public sealed class CurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public int UserId => int.TryParse(accessor.HttpContext?.User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException("An authenticated user is required.");
}
