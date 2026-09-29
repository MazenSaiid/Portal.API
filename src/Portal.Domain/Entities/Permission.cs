namespace Portal.Domain.Entities;

/// <summary>
/// A permission row mirrors an entry of <see cref="Authorization.PermissionRegistry"/>.
/// Rows are synchronised from code at startup; only the role grants are edited at runtime.
/// </summary>
public class Permission
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
