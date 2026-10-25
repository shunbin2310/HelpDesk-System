using System.Text;
using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;

namespace HelpDesk.Api.Authentication;

public static class BootstrapAdminCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() || Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Run bootstrap-admin in an interactive local Development terminal.");
            return 1;
        }
        Console.Write("Email: ");
        var email = Console.ReadLine() ?? "";
        Console.Write("Full name: ");
        var name = Console.ReadLine() ?? "";
        Console.Write("Password (10-128 characters, uppercase, lowercase, number and symbol): ");
        var password = ReadPassword();
        Console.Write("Confirm password: ");
        if (password != ReadPassword())
        {
            Console.Error.WriteLine("Passwords do not match. No account was created.");
            return 1;
        }
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUserAccountStore>()
            .BootstrapAdminAsync(new(email, name, password, ApplicationRoles.SystemAdmin));
        Console.WriteLine(result.Succeeded ? "System Admin created. You can now sign in." : string.Join(Environment.NewLine, result.Errors));
        return result.Succeeded ? 0 : 1;
    }

    private static string ReadPassword()
    {
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return value.ToString();
            }
            if (key.Key == ConsoleKey.Backspace && value.Length > 0)
                value.Length--;
            else if (!char.IsControl(key.KeyChar) && value.Length < 1024)
                value.Append(key.KeyChar);
        }
    }
}
