using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("tickets")]
public class Ticket : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; }

    [Column("title")]
    public string Title { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("status_id")]
    public int StatusId { get; set; }

    [Column("priority_id")]
    public int PriorityId { get; set; }

    [Column("zone_id")]
    public string ZoneId { get; set; }

    [Column("creator_id")]
    public string CreatorId { get; set; }

    [Column("assignee_id")]
    public string? AssigneeId { get; set; }

    [Column("resolution_note")]
    public string? ResolutionNote { get; set; } 

    [Column("verified_at")]
    public DateTime? VerifiedAt { get; set; } 

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } 

    [Column("closed_at")]
    public DateTime? ClosedAt { get; set; }
}