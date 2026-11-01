using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

[Table("conversations")]
public class Conversation : BaseModel
{
    [PrimaryKey("id", false)] public string Id { get; set; }
    [Column("type")] public string Type { get; set; } = "private";
    [Column("title")] public string? Title { get; set; }

    [Column("creator_id")] public string? CreatorId { get; set; } 
    [Column("aes_key")] public string AesKey { get; set; }

    [Column("created_at")] public DateTime? CreatedAt { get; set; }
}