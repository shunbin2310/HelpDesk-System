using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HelpDesk.Application.Authentication;
using HelpDesk.Application.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Infrastructure.Authentication;

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    public LoginResponse Create(UserAccount user, string securityStamp)
    {
        var settings = options.Value;
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        Claim[] claims = [new("sub", user.Id.ToString(CultureInfo.InvariantCulture)),
            new("name", user.FullName), new("email", user.Email), new("role", user.Role),
            new("security_stamp", securityStamp), new("jti", Guid.NewGuid().ToString("N"))];
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, now.UtcDateTime,
            expires.UtcDateTime, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires, user);
    }
}
