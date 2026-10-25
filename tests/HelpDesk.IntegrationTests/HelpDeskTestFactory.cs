using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;

namespace HelpDesk.IntegrationTests;

internal sealed class HelpDeskTestFactory(string connection) : WebApplicationFactory<Program>
{
    public string AttachmentRoot { get; } = Path.Combine(Path.GetTempPath(), "HelpDeskAttachmentsTests", Guid.NewGuid().ToString("N"));
    public TimeProvider Clock { get; set; } = TimeProvider.System;
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["Database:ExpectedDatabase"] = new SqlConnectionStringBuilder(connection).InitialCatalog,
            ["DelegationProcessing:Enabled"] = "false",
            ["Attachments:RootPath"] = AttachmentRoot,
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Logging:LogLevel:Default"] = "Warning"
        }));
        builder.ConfigureServices(services => services.AddSingleton(Clock));
        return base.CreateHost(builder);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(AttachmentRoot))
            Directory.Delete(AttachmentRoot, recursive: true);
    }
}
