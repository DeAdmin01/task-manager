using System;
using System.Threading.Tasks;
using System.Text.Json; 

namespace SmartArena.Interfaces
{
    public interface IAlertService
    {
        Task<bool> ShowAlertAsync(string title, string message, string confirmText, string cancelText = null);
    }
}

namespace SmartArena.Services
{
    public class BlazorAlertService : Interfaces.IAlertService
    {
        public event Action? OnShow;
        public event Action? OnHide;

        public string Title { get; private set; } = "";
        public string Message { get; private set; } = "";
        public string ConfirmText { get; private set; } = "OK";
        public string? CancelText { get; private set; }
        public bool ShowCancel => !string.IsNullOrEmpty(CancelText);

        private TaskCompletionSource<bool>? _tcs;

        public Task<bool> ShowAlertAsync(string title, string message, string confirmText, string cancelText = null)
        {
            Title = title;
            Message = ParseErrorMessage(message); // <-- ВЫЗЫВАЕМ ПАРСЕР ОШИБОК
            ConfirmText = confirmText;
            CancelText = cancelText;

            _tcs = new TaskCompletionSource<bool>();
            OnShow?.Invoke();

            return _tcs.Task;
        }

        public void Confirm()
        {
            OnHide?.Invoke();
            _tcs?.TrySetResult(true);
        }

        public void Cancel()
        {
            OnHide?.Invoke();
            _tcs?.TrySetResult(false);
        }

        private string ParseErrorMessage(string rawMessage)
        {
            if (string.IsNullOrWhiteSpace(rawMessage)) return rawMessage;

            // Ищем границы JSON объекта, если он есть в строке
            int jsonStart = rawMessage.IndexOf('{');
            int jsonEnd = rawMessage.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                // Разделяем строку на: текст до JSON, сам JSON, текст после JSON
                string textBefore = rawMessage.Substring(0, jsonStart);
                string jsonPart = rawMessage.Substring(jsonStart, jsonEnd - jsonStart + 1);
                string textAfter = rawMessage.Substring(jsonEnd + 1);

                try
                {
                    using var doc = JsonDocument.Parse(jsonPart);
                    
                    // Если это ошибка от Supabase, в ней есть поле "message"
                    if (doc.RootElement.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                    {
                        string parsedMsg = msgProp.GetString() ?? "";

                        // Убираем английский технический префикс, который мы ставим в триггерах SQL
                        if (parsedMsg.StartsWith("Access Denied: "))
                        {
                            parsedMsg = parsedMsg.Substring("Access Denied: ".Length).Trim();
                        }

                        // Склеиваем красивое сообщение
                        return textBefore + parsedMsg + textAfter;
                    }
                }
                catch
                {
                    // Если JSON оказался "битым" или не распарсился, просто пропускаем
                }
            }

            // Возвращаем как есть, если это был обычный текст без JSON
            return rawMessage;
        }
    }
}