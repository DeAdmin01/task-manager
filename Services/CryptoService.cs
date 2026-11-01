using System.Security.Cryptography;
using System.Text;

namespace SmartArena.Services
{
    public class CryptoService
    {
        // Генерация нового случайного ключа для нового чата
        public string GenerateAesKey()
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            return Convert.ToBase64String(aes.Key);
        }

        // Шифрование сообщения
        public string EncryptMessage(string plainText, string base64Key)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            using var aes = Aes.Create();
            aes.Key = Convert.FromBase64String(base64Key);
            aes.GenerateIV(); // Уникальный IV для каждого сообщения

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();

            // Записываем IV в начало потока (16 байт) - он понадобится при расшифровке
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            // Возвращаем склеенный IV + Зашифрованный текст в формате Base64
            return Convert.ToBase64String(ms.ToArray());
        }

        // Расшифровка сообщения
        public string DecryptMessage(string cipherTextBase64, string base64Key)
        {
            if (string.IsNullOrEmpty(cipherTextBase64)) return "";

            try
            {
                var fullCipher = Convert.FromBase64String(cipherTextBase64);

                using var aes = Aes.Create();
                aes.Key = Convert.FromBase64String(base64Key);

                // Извлекаем IV из первых 16 байт
                var iv = new byte[aes.BlockSize / 8];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                aes.IV = iv;

                // Читаем оставшуюся часть (само зашифрованное сообщение)
                using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);

                return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка расшифровки: {ex.Message}");
                return "[Ошибка расшифровки сообщения]";
            }
        }
    }
}