namespace PAV.Core.Services;

public static class AuthRules
{
    public static readonly string[] KnownDefaultPasswords = ["admin", "engineer", "guest"];

    public static bool IsKnownDefaultPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;
        var p = password.Trim();
        return KnownDefaultPasswords.Any(d => d.Equals(p, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ValidateNewPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 8)
            return "Password must be at least 8 characters.";
        if (IsKnownDefaultPassword(password))
            return "Do not use a known default password (admin, engineer, or guest).";
        return null;
    }
}
