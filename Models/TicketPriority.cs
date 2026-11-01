using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("ticket_priorities")]
public class TicketPriority : BaseModel
{
    [PrimaryKey("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("weight")]
    public int Weight { get; set; } 

    [Column("hex_color")]
    public string HexColor { get; set; }
}