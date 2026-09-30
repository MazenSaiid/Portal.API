namespace Portal.Domain.Authorization;

/// <summary>
/// Permission keys checked by the API. Add a new nested class per module and register
/// its keys in <see cref="PermissionRegistry"/>; they appear in the UI automatically.
/// </summary>
public static class Permissions
{
    public static class Tickets
    {
        public const string View = "Tickets.View";
        public const string Create = "Tickets.Create";
        public const string Edit = "Tickets.Edit";
        public const string Work = "Tickets.Work";
        public const string Assign = "Tickets.Assign";
        public const string Escalate = "Tickets.Escalate";
        public const string Delete = "Tickets.Delete";
        public const string ManageCategories = "Tickets.ManageCategories";
    }

    public static class Users
    {
        public const string View = "Users.View";
        public const string Create = "Users.Create";
        public const string Edit = "Users.Edit";
        public const string Delete = "Users.Delete";
    }

    public static class Dashboard
    {
        public const string View = "Dashboard.View";
    }

    public static class QuickReplies
    {
        public const string Manage = "QuickReplies.Manage";
    }

    public static class Customers
    {
        public const string View = "Customers.View";
        public const string Create = "Customers.Create";
        public const string Edit = "Customers.Edit";
        public const string Delete = "Customers.Delete";
        public const string AddActivity = "Customers.AddActivity";
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
