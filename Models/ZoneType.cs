using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

[Table("zone_types")]
public class ZoneType : BaseModel
{
    [PrimaryKey("id")] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("description")] public string? Description { get; set; }
}