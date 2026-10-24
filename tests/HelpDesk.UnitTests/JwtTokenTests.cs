using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.UnitTests;

public sealed class JwtTokenTests
{
    private static JwtOptions Settings() => new() { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)) };

    [Fact]
    public void TokenContainsTheSingleRoleAndValidatesOnlyWithExpectedIssuerAudienceAndKey()
    {
        var options = Settings();
        var user = new UserAccount(42, "test@example.invalid", "Test user", ApplicationRoles.Employee, true);
        var result = new JwtTokenService(Options.Create(options)).Create(user, "stamp");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(result.AccessToken, options.ValidationParameters(), out _);
        Assert.Equal("42", principal.FindFirst("sub")?.Value);
        Assert.Equal("stamp", principal.FindFirst("security_stamp")?.Value);
        Assert.Equal(ApplicationRoles.Employee, Assert.Single(principal.FindAll("role")).Value);
        Assert.True(principal.IsInRole(ApplicationRoles.Employee));
        Assert.InRange((result.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes, 59, 61);
        var wrongIssuer = options.ValidationParameters(); wrongIssuer.ValidIssuer = "another";
        Assert.Throws<SecurityTokenInvalidIssuerException>(() => handler.ValidateToken(result.AccessToken, wrongIssuer, out _));
        var wrongAudience = options.ValidationParameters(); wrongAudience.ValidAudience = "another";
        Assert.Throws<SecurityTokenInvalidAudienceException>(() => handler.ValidateToken(result.AccessToken, wrongAudience, out _));
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(result.AccessToken, Settings().ValidationParameters(), out _));
    }

    [Fact]
    public void ExpiredAndUnsignedTokensAreRejected()
    {
        var options = Settings();
        var credentials = new SigningCredentials(options.ValidationParameters().IssuerSigningKey, SecurityAlgorithms.HmacSha256);
        var handler = new JwtSecurityTokenHandler();
        var expired = handler.WriteToken(new JwtSecurityToken(options.Issuer, options.Audience, [],
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1), credentials));
        Assert.Throws<SecurityTokenExpiredException>(() => handler.ValidateToken(expired, options.ValidationParameters(), out _));
        var unsigned = handler.WriteToken(new JwtSecurityToken(options.Issuer, options.Audience, [],
            DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(unsigned, options.ValidationParameters(), out _));
    }

    [Theory]
    [InlineData("", 60)]
    [InlineData("short", 60)]
    [InlineData("                                ", 60)]
    [InlineData("a-development-key-with-at-least-32-bytes", 0)]
    [InlineData("a-development-key-with-at-least-32-bytes", 481)]
    public void InvalidConfigurationIsRejected(string key, int minutes) =>
        Assert.False(new JwtOptions { SigningKey = key, AccessTokenMinutes = minutes }.IsValid());
}
