using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "HelpDesk.Api";
    public string Audience { get; set; } = "HelpDesk.Web";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;

    public bool IsValid() => !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience)
        && !string.IsNullOrWhiteSpace(SigningKey) && Encoding.UTF8.GetByteCount(SigningKey) >= 32
        && AccessTokenMinutes is >= 5 and <= 480;

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = Issuer,
        ValidateAudience = true, ValidAudience = Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        NameClaimType = "name", RoleClaimType = "role", ClockSkew = TimeSpan.FromSeconds(30)
    };
}
