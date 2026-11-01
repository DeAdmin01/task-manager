using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

[Table("stadium_zones")]
public class StadiumZone : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("type_id")] public int? TypeId { get; set; }
    [Column("capacity")] public int Capacity { get; set; }
    [Column("parent_zone_id")] public Guid? ParentZoneId { get; set; }
}