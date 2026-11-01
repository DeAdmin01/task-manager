using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("sensor_data")]
public class SensorData : BaseModel
{
    [PrimaryKey("id")] // Здесь автоинкремент (BIGSERIAL), поэтому оставляем по умолчанию
    public long Id { get; set; }

    [Column("sensor_type_id")]
    public int SensorTypeId { get; set; }

    [Column("zone_id")]
    public string ZoneId { get; set; }

    [Column("value")]
    public double Value { get; set; }

    [Column("recorded_at")]
    public DateTime RecordedAt { get; set; }
}