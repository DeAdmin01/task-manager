using SmartArena.Models;
using Supabase.Postgrest;

namespace SmartArena.Services;

public class ChatService
{
    private readonly Supabase.Client _supabase;
    private readonly CryptoService _crypto;

    // Внедряем CryptoService через конструктор
    public ChatService(SupabaseService service, CryptoService crypto)
    {
        _supabase = service.Client;
        _crypto = crypto;
    }

    public async Task<List<Conversation>> GetMyChatsAsync(string myUserId)
    {
        var response = await _supabase.From<Conversation>().Get();
        return response.Models.OrderByDescending(c => c.CreatedAt).ToList();
    }

    public async Task<Conversation> CreateChatAsync(string myUserId, string type, string? title, List<string> participantIds)
    {
        var chat = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Title = title,
            CreatorId = myUserId,
            AesKey = _crypto.GenerateAesKey()
        };

        // 1. Создаем чат и СОХРАНЯЕМ ОТВЕТ БАЗЫ, чтобы взять РЕАЛЬНЫЙ сгенерированный ID
        var response = await _supabase.From<Conversation>().Insert(chat);
        var insertedChat = response.Models.FirstOrDefault();

        // Если база ничего не вернула, значит что-то пошло не так
        if (insertedChat == null || string.IsNullOrEmpty(insertedChat.Id))
            throw new Exception("Чат был создан, но база данных не вернула его ID.");

        // 2. Формируем список участников, используя ИМЕННО ТОТ ID, который дала база (insertedChat.Id)
        var allParticipants = new List<ConversationParticipant>
        {
            // Добавляем вас как админа
            new ConversationParticipant { ConversationId = insertedChat.Id, UserId = myUserId, IsAdmin = true }
        };

        // Добавляем остальных
        foreach (var pId in participantIds)
        {
            allParticipants.Add(new ConversationParticipant { ConversationId = insertedChat.Id, UserId = pId, IsAdmin = false });
        }

        // 3. Вставляем участников с опцией Minimal (чтобы избежать старых ошибок)
        var options = new QueryOptions { Returning = QueryOptions.ReturnType.Minimal };
        await _supabase.From<ConversationParticipant>().Insert(allParticipants, options);

        // Возвращаем реальный чат из базы
        return insertedChat;
    }

    // ИСПРАВЛЕННЫЙ МЕТОД УДАЛЕНИЯ
    public async Task DeleteChatAsync(string chatId)
    {
        var options = new QueryOptions { Returning = QueryOptions.ReturnType.Minimal };
        await _supabase.From<Conversation>()
            .Where(c => c.Id == chatId)
            .Delete(options);
    }

    // НОВЫЙ МЕТОД: Добавление участников в существующий чат
    public async Task AddParticipantsAsync(string chatId, List<string> participantIds)
    {
        var parts = new List<ConversationParticipant>();
        foreach (var pId in participantIds)
        {
            parts.Add(new ConversationParticipant { ConversationId = chatId, UserId = pId, IsAdmin = false });
        }
        var options = new QueryOptions { Returning = QueryOptions.ReturnType.Minimal };
        await _supabase.From<ConversationParticipant>().Insert(parts, options);
    }

    // НОВЫЙ МЕТОД: Удаление участника из чата
    public async Task RemoveParticipantAsync(string chatId, string userId)
    {
        var options = new QueryOptions { Returning = QueryOptions.ReturnType.Minimal };
        await _supabase.From<ConversationParticipant>()
            .Where(p => p.ConversationId == chatId && p.UserId == userId)
            .Delete(options);
    }

    // НОВЫЙ МЕТОД: Получение участников конкретного чата
    public async Task<List<ConversationParticipant>> GetParticipantsAsync(string chatId)
    {
        var response = await _supabase.From<ConversationParticipant>()
            .Where(p => p.ConversationId == chatId)
            .Get();
        return response.Models;
    }

    // Получение и расшифровка сообщений
    public async Task<List<ChatMessage>> GetMessagesAsync(Conversation chat)
    {
        var response = await _supabase.From<ChatMessage>()
            .Where(m => m.ConversationId == chat.Id)
            .Get();

        var msgs = response.Models.OrderBy(m => m.CreatedAt).ToList();

        // Расшифровываем каждое сообщение, используя ключ ЭТОГО чата
        foreach (var m in msgs)
        {
            m.EncryptedContent = _crypto.DecryptMessage(m.EncryptedContent, chat.AesKey);
        }
        return msgs;
    }

    // Шифрование и отправка
    // ИСПРАВЛЕННЫЙ МЕТОД: Отправка сообщений (Добавлено явное указание времени)
    public async Task SendMessageAsync(Conversation chat, string senderId, string text)
    {
        string cipherText = _crypto.EncryptMessage(text, chat.AesKey);

        var msg = new ChatMessage
        {
            ConversationId = chat.Id,
            SenderId = senderId,
            EncryptedContent = cipherText,
            IsRead = false,
            CreatedAt = DateTime.UtcNow // ЯВНО УКАЗЫВАЕМ ВРЕМЯ
        };
        await _supabase.From<ChatMessage>().Insert(msg);
    }

    // НОВЫЙ МЕТОД: Отметка сообщений как прочитанных
    public async Task MarkMessagesAsReadAsync(string chatId, string myUserId)
    {
        var options = new QueryOptions { Returning = QueryOptions.ReturnType.Minimal };

        // Условие m.SenderId != myUserId гарантирует, что ваши сообщения 
        // не будут помечаться прочитанными вами самими.
        await _supabase.From<ChatMessage>()
            .Where(m => m.ConversationId == chatId && m.SenderId != myUserId && m.IsRead == false)
            .Set(m => m.IsRead, true)
            .Update(options);
    }
    // Метод для переименования чата
    public async Task RenameChatAsync(string chatId, string newTitle)
    {
        var options = new QueryOptions { Returning = QueryOptions.ReturnType.Minimal };

        // Точечно обновляем только поле Title, не трогая остальной объект
        await _supabase.From<Conversation>()
            .Where(c => c.Id == chatId)
            .Set(c => c.Title, newTitle)
            .Update(options);
    }
}