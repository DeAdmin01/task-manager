using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

[Table("messages")]
public class ChatMessage : BaseModel
{
    [PrimaryKey("id", false)] public long Id { get; set; }
    [Column("conversation_id")] public string ConversationId { get; set; }
    [Column("sender_id")] public string SenderId { get; set; }
    [Column("encrypted_content")] public string EncryptedContent { get; set; }
    [Column("is_read")] public bool IsRead { get; set; }

    [Column("created_at")] public DateTime? CreatedAt { get; set; } // Сделали nullable
}