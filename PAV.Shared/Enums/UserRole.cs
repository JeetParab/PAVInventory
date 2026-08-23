namespace PAV.Shared.Enums;

public enum UserRole
{
    Guest = 0,
    Engineer = 1,
    Administrator = 2
}

public static class UserRoleNames
{
    public static string Display(this UserRole role) => role switch
    {
        UserRole.Guest => "Guest",
        UserRole.Engineer => "Engineer",
        UserRole.Administrator => "Administrator",
        _ => role.ToString()
    };
}

public static class Permissions
{
    public const string View = "View";
    public const string Add = "Add";
    public const string Edit = "Edit";
    public const string Delete = "Delete";
    public const string Assign = "Assign";
    public const string Import = "Import";
    public const string Export = "Export";
    public const string ManageUsers = "ManageUsers";
    public const string ManageLocations = "ManageLocations";
    public const string ManageCategories = "ManageCategories";
    public const string Backup = "Backup";
    public const string Restore = "Restore";

    public static IReadOnlyList<string> ForRole(UserRole role) => role switch
    {
        UserRole.Administrator =>
        [
            View, Add, Edit, Delete, Assign, Import, Export,
            ManageUsers, ManageLocations, ManageCategories, Backup, Restore
        ],
        UserRole.Engineer =>
        [
            View, Add, Edit, Assign, Import, Export
        ],
        _ =>
        [
            View, Export
        ]
    };

    public static bool Allows(UserRole role, string permission) =>
        ForRole(role).Contains(permission);
}
