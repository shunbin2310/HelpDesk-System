using System.Security.Claims;
using HelpDesk.Application.Authentication;
using HelpDesk.Infrastructure.Authentication;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.DependencyInjection;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddHelpDeskAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("Jwt"))
            .Validate(x => x.IsValid(), "Jwt requires Issuer, Audience, at least 32 bytes of SigningKey and a lifetime of 5-480 minutes.")
            .ValidateOnStart();
        services.AddScoped<JwtTokenService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwt) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = jwt.Value.ValidationParameters();
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        if (!int.TryParse(context.Principal?.FindFirstValue("sub"), out var id))
                        {
                            context.Fail("Invalid user.");
                            return;
                        }
                        var stamp = context.Principal?.FindFirstValue("security_stamp");
                        var db = context.HttpContext.RequestServices.GetRequiredService<HelpDeskDbContext>();
                        if (string.IsNullOrEmpty(stamp) || !await db.Users.AsNoTracking().AnyAsync(
                                x => x.Id == id && x.IsActive && x.SecurityStamp == stamp, context.HttpContext.RequestAborted))
                            context.Fail("Session is no longer valid.");
                    }
                };
            });
        services.AddAuthorization();
        return services;
    }
}
