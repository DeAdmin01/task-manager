using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("profiles")]
public class Profile : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; }

    [Column("full_name")]
    public string? FullName { get; set; }

    [Column("role_id")]
    public int? RoleId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }
}