using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("permissions")]
public class Permission : BaseModel
{
    [PrimaryKey("id")]
    public int Id { get; set; }

    [Column("codename")]
    public string Codename { get; set; }

    [Column("description")]
    public string Description { get; set; }
}