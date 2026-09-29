namespace Portal.Domain.Authorization;

public sealed record PermissionDefinition(string Key, string Module, string Description);

/// <summary>Single source of truth for every permission the system understands.</summary>
public static class PermissionRegistry
{
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Permissions.Customers.View, "Customers", "View customers, contacts, interactions, notes and attachments"),
        new(Permissions.Customers.Create, "Customers", "Create customers"),
        new(Permissions.Customers.Edit, "Customers", "Edit customer profiles and contacts; manage anyone's notes and files"),
        new(Permissions.Customers.Delete, "Customers", "Delete customers"),
        new(Permissions.Customers.AddActivity, "Customers", "Log interactions, add notes and upload attachments"),

        new(Permissions.Users.View, "Users", "View users"),
        new(Permissions.Users.Create, "Users", "Create users"),
        new(Permissions.Users.Edit, "Users", "Edit users, change status and reset passwords"),
        new(Permissions.Users.Delete, "Users", "Delete users"),

        new(Permissions.Roles.View, "Roles", "View roles and their permissions"),
        new(Permissions.Roles.Create, "Roles", "Create roles"),
        new(Permissions.Roles.Edit, "Roles", "Rename and describe roles"),
        new(Permissions.Roles.Delete, "Roles", "Delete roles"),
        new(Permissions.Roles.ManagePermissions, "Roles", "Grant and revoke role permissions"),
    ];
}
