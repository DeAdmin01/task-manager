using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("sensor_types")]
public class SensorType : BaseModel
{
    [PrimaryKey("id")]
    public int Id { get; set; }

    [Column("unit_id")]
    public int UnitId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("description")]
    public string Description { get; set; }
}