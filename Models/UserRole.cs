using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("roles")]
public class UserRole : BaseModel
{
    [PrimaryKey("id")]
    public int? Id { get; set; }

    [Column("name")]
    public string? Name { get; set; }
}