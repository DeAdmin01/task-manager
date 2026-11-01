using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("role_permissions")]
public class RolePermission : BaseModel
{
    [PrimaryKey("role_id", false)]
    [Column("role_id")]
    public int RoleId { get; set; }

    [PrimaryKey("permission_id", false)]
    [Column("permission_id")]
    public int PermissionId { get; set; }
}