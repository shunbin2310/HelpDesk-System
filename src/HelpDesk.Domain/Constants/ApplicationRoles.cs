namespace HelpDesk.Domain.Constants;

public static class ApplicationRoles
{
    public const string Employee = "EMPLOYEE";
    public const string SupportITStaff = "SUPPORT_IT_STAFF";
    public const string SupportAdmin = "SUPPORT_ADMIN";
    public const string SystemAdmin = "SYSTEM_ADMIN";

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(new[] { Employee, SupportITStaff, SupportAdmin, SystemAdmin });

    public static bool IsValid(string role) => All.Contains(role, StringComparer.Ordinal);
}
