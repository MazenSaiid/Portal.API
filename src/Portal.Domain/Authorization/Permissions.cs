namespace Portal.Domain.Authorization;

/// <summary>
/// Permission keys checked by the API. Add a new nested class per module and register
/// its keys in <see cref="PermissionRegistry"/>; they appear in the UI automatically.
/// </summary>
public static class Permissions
{
    public static class Users
    {
        public const string View = "Users.View";
        public const string Create = "Users.Create";
        public const string Edit = "Users.Edit";
        public const string Delete = "Users.Delete";
    }

    public static class Roles
    {
        public const string View = "Roles.View";
        public const string Create = "Roles.Create";
        public const string Edit = "Roles.Edit";
        public const string Delete = "Roles.Delete";
        public const string ManagePermissions = "Roles.ManagePermissions";
    }
}
