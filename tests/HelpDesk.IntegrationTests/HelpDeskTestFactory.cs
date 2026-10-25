using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HelpDesk.IntegrationTests;

internal sealed class HelpDeskTestFactory(string connection) : WebApplicationFactory<Program>
{
    public string AttachmentRoot { get; } = Path.Combine(Path.GetTempPath(), "HelpDeskAttachmentsTests", Guid.NewGuid().ToString("N"));
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["Database:MigrateOnStartup"] = "false",
            ["Attachments:RootPath"] = AttachmentRoot,
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Logging:LogLevel:Default"] = "Warning"
        }));
        return base.CreateHost(builder);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(AttachmentRoot)) Directory.Delete(AttachmentRoot, recursive: true);
    }
}
