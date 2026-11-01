using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SmartArena.Models;

[Table("conversation_participants")]
public class ConversationParticipant : BaseModel
{
    [PrimaryKey("conversation_id", false)][Column("conversation_id")] public string ConversationId { get; set; }
    [PrimaryKey("user_id", false)][Column("user_id")] public string UserId { get; set; }
    [Column("joined_at")] public DateTime JoinedAt { get; set; }
    [Column("is_admin")] public bool IsAdmin { get; set; }
}