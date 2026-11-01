using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

namespace SmartArena.Models
{
    [Table("notifications")]
    public class Notification : BaseModel
    {
        [PrimaryKey("id", false)] public Guid Id { get; set; }
        [Column("user_id")] public string UserId { get; set; } = "";
        [Column("title")] public string Title { get; set; } = "";
        [Column("body")] public string Body { get; set; } = "";
        [Column("type")] public string Type { get; set; } = "";

        // JSON в виде строки
        [Column("payload")] public string Payload { get; set; } = "{}";

        [Column("is_read")] public bool IsRead { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}