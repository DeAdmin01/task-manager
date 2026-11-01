using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

namespace SmartArena.Models
{
    [Table("ticket_history")]
    public class TicketHistory : BaseModel
    {
        [PrimaryKey("id", false)] public long Id { get; set; }
        [Column("ticket_id")] public string TicketId { get; set; } = "";
        [Column("changed_by")] public string? ChangedBy { get; set; }
        [Column("old_status_id")] public int? OldStatusId { get; set; }
        [Column("new_status_id")] public int? NewStatusId { get; set; }
        [Column("comment")] public string? Comment { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}