using SmartArena.Models;
using Supabase.Realtime;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.PostgresChanges;
using System.Diagnostics;
using static Supabase.Postgrest.Constants;

namespace SmartArena.Services
{
    /// <summary>
    /// Служба авториазции(работы с данными пользователя)
    /// </summary>
    public class AuthService
    {
        private readonly Supabase.Client _supabase;
        
        // Ключи для безопасного хранилища
        private const string AccessTokenKey = "sb_access_token";
        private const string RefreshTokenKey = "sb_refresh_token";

        public AuthService(SupabaseService service)
        {
            _supabase = service.Client;
        }

        // Свойство для получения текущего юзера (удобно для ViewModel)
        public Supabase.Gotrue.User? CurrentUser => _supabase.Auth.CurrentUser;

        #region Session Management (Запомнить меня)

        /// <summary>
        /// Восстановление сессии
        /// </summary>
        /// <returns>Статус о восстановлении(успешно или нет)</returns>
        public async Task<bool> RestoreSessionAsync()
        {
            try
            {
                var accessToken = await SecureStorage.GetAsync(AccessTokenKey);
                var refreshToken = await SecureStorage.GetAsync(RefreshTokenKey);

                if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
                {
                    // Восстанавливаем сессию в клиенте Supabase
                    await _supabase.Auth.SetSession(accessToken, refreshToken);
                    
                    // Проверяем, жив ли токен (опционально можно сделать RefreshSession)
                    return _supabase.Auth.CurrentUser != null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка восстановления сессии: {ex.Message}");
                // Если токены битые, чистим их
                await LogoutAsync();
            }
            return false;
        }

        /// <summary>
        /// Сохранение сессии
        /// </summary>
        /// <returns>Сохраняют токены сессии пользователя</returns>
        private async Task SaveSessionAsync()
        {
            var session = _supabase.Auth.CurrentSession;
            if (session != null && session.AccessToken != null && session.RefreshToken != null)
            {
                await SecureStorage.SetAsync(AccessTokenKey, session.AccessToken);
                await SecureStorage.SetAsync(RefreshTokenKey, session.RefreshToken);
            }
        }
        /// <summary>
        /// Очистка данных сессии с устройства
        /// </summary>
        private void ClearLocalSession()
        {
            SecureStorage.Remove(AccessTokenKey);
            SecureStorage.Remove(RefreshTokenKey);
        }

        #endregion

        #region Authentication
        /// <summary>
        /// Авторизация
        /// </summary>
        /// <param name="email">Почта</param>
        /// <param name="password">Пароль</param>
        /// <returns>Статус авторизации(успешно или нет)</returns>
        public async Task<bool> Login(string email, string password)
        {
            try 
            {
                var session = await _supabase.Auth.SignIn(email, password);
                
                if (session?.User != null)
                {
                    await SaveSessionAsync(); // Сохраняем токены
                    return true;
                }
                return false;
            }
            catch
            {
                throw; // Пробрасываем ошибку во ViewModel для отображения UI
            }
        }
        /// <summary>
        /// Регистрация
        /// </summary>
        /// <param name="email">Почта</param>
        /// <param name="password">Пароль</param>
        /// <returns>Статус регистрации</returns>
        public async Task<bool> SignUp(string email, string password)
        {
            try 
            {
                var session = await _supabase.Auth.SignUp(email, password);
                
                // Supabase может вернуть сессию сразу после регистрации (если выключено подтверждение почты)
                if (session?.User != null)
                {
                    if (session.AccessToken != null) 
                        await SaveSessionAsync();
                    
                    return true;
                }
                return false;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// Выход(завершение сессиии)
        /// </summary>
        /// <returns>Статус завершения</returns>
        public async Task LogoutAsync()
        {
            try 
            {
                await _supabase.Auth.SignOut();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при выходе на сервере: {ex.Message}");
            }
            finally
            {
                ClearLocalSession();
            }
        }

        #endregion

        #region Password Reset
        /// <summary>
        /// СБрос пароля
        /// </summary>
        /// <param name="email">Почта</param>
        /// <returns>Статус выполнения операции</returns>
        public async Task<bool> SendPasswordResetCode(string email)
        {
            try
            {
                await _supabase.Auth.ResetPasswordForEmail(email);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка сброса пароля: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// Сброс пароля при помощи OTP
        /// </summary>
        /// <param name="email">Почта</param>
        /// <param name="code">Код</param>
        /// <param name="newPassword">Новый пароль</param>
        /// <returns>Статус сброса</returns>
        public async Task<bool> VerifyCodeAndSetPassword(string email, string code, string newPassword)
        {
            try
            {
                // 1. Подтверждаем OTP
                var session = await _supabase.Auth.VerifyOTP(email, code, Supabase.Gotrue.Constants.EmailOtpType.Recovery);

                if (session?.AccessToken != null)
                {
                    // Важно: После VerifyOTP мы автоматически авторизованы.
                    // Обновляем токен в хранилище, так как у нас новая сессия
                    await SaveSessionAsync();

                    // 2. Обновляем пароль пользователя
                    var attrs = new Supabase.Gotrue.UserAttributes { Password = newPassword };
                    await _supabase.Auth.Update(attrs);
                    
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка установки нового пароля: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Realtime
        /// <summary>
        /// Адаптивное обновление данных профиля
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="callback">Профиль</param>
        /// <returns>Ожидание ответа</returns>
        public async Task<RealtimeChannel> SubscribeToProfileUpdatesAsync(string userId, Action<Profile> callback)
        {
            var channel = await _supabase.From<Profile>()
                .On(PostgresChangesOptions.ListenType.Updates, (IRealtimeChannel _, PostgresChangesResponse change) =>
                {
                    try
                    {
                        var profile = change.Model<Profile>();
                        if (profile != null && profile.Id == userId)
                        {
                            MainThread.BeginInvokeOnMainThread(() => callback(profile));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in realtime: {ex.Message}");
                    }
                });

            return channel;
        }


        /// <summary>
        /// Отписка от обновлений
        /// </summary>
        private class ActionDisposable : IDisposable
        {
            private Action _action;
            public ActionDisposable(Action action) => _action = action;
            public void Dispose() => _action?.Invoke();
        }

        #endregion
        /// <summary>
        /// Получение профиля пользователя
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>Профиль пользователя</returns>
        public async Task<Profile?> GetUserProfileAsync(string userId)
        {
            try
            {
                // Выбираем id и is_active. 
                var response = await _supabase
                    .From<Profile>()
                    .Select("id, is_active")
                    .Filter("id", Operator.Equals, userId)
                    .Get();

                // Возвращаем первого найденного пользователя или null
                return response.Models.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка получения профиля: {ex.Message}");
                return null;
            }
        }

       /// <summary>
       /// Получение профиля с ролью
       /// </summary>
       /// <returns>Профиль с ролью</returns>
        public async Task<(Profile? Profile, string RoleName, string RoleID)> GetCurrentUserProfileWithRoleAsync()
        {
            try
            {
                var userId = CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId)) return (null, "Гость", "0");

                // 1. Получаем профиль
                var profileResponse = await _supabase
                    .From<Profile>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, userId)
                    .Single();

                var profile = profileResponse; 

                if (profile == null) return (null, "Нет профиля", "0");

                var roleResponse = await _supabase
                    .From<UserRole>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, profile.RoleId)
                    .Single();

                var roleName = roleResponse?.Name ?? "Неизвестно";

                return (profile, roleName, userId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки данных пользователя: {ex.Message}");
                return (null, "Ошибка", "0");
            }
        }

        /// <summary>
        /// Проверка наличия прав у пользователя
        /// </summary>
        /// <param name="permissionCodename">Разрешение</param>
        /// <returns>Наличие разрешения</returns>
        public async Task<bool> UserHasPermissionAsync(string permissionCodename)
        {
            try
            {
                var userId = CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId)) return false;

                // 1. Узнаем RoleID пользователя
                var profileResponse = await _supabase
                    .From<Profile>()
                    .Select("role_id")
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, userId)
                    .Single();

                if (profileResponse == null) return false;
                int? roleId = profileResponse.RoleId;

                var rpcParams = new Dictionary<string, object>
        {
            { "required_permission", permissionCodename }
        };

                var response = await _supabase.Rpc("has_permission", rpcParams);

                return response.Content.Contains("true");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка проверки прав: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Обновление данных профиля(полное имя)
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="fullName">Новое полное имя</param>
        /// <returns>Статус обновления</returns>
        public async Task<bool> UpdateProfileDataAsync(string userId, string fullName)
        {
            try
            {
                await _supabase.From<Profile>()
                    .Where(x => x.Id == userId)
                    .Set(x => x.FullName, fullName) 
                    .Update();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обновления профиля: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// Обновление критических данных
        /// </summary>
        /// <param name="newEmail">Новая почта</param>
        /// <param name="newPassword">Новый пароль</param>
        /// <returns>Статус обновления</returns>
        public async Task<bool> UpdateAuthDataAsync(string? newEmail, string? newPassword)
        {
            try
            {
                var attrs = new Supabase.Gotrue.UserAttributes();

                if (!string.IsNullOrWhiteSpace(newEmail))
                    attrs.Email = newEmail;

                if (!string.IsNullOrWhiteSpace(newPassword))
                    attrs.Password = newPassword;

                // Отправляем запрос в Supabase Auth
                var response = await _supabase.Auth.Update(attrs);

                return response != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обновления Auth: {ex.Message}");
                throw; // Пробрасываем ошибку, чтобы ViewModel могла показать её (например "Weak Password")
            }
        }
        /// <summary>
        /// Asynchronously retrieves all user profiles from the data source.
        /// </summary>
        /// <remarks>If an error occurs during retrieval, the method returns an empty list rather than
        /// throwing an exception. The returned list may be empty if no profiles exist in the data source.</remarks>
        /// <returns>A list of <see cref="Profile"/> objects representing all user profiles. Returns an empty list if the
        /// retrieval fails or no profiles are found.</returns>
        public async Task<List<Profile>> GetAllProfilesAsync()
        {
            try
            {
                var response = await _supabase.From<Profile>().Get();
                return response.Models;
            }
            catch (Exception ex)
            {
                // Теперь вы увидите причину ошибки в окне "Вывод" (Output) в Visual Studio
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки профилей: {ex.Message}");
                return new List<Profile>();
            }
        }


        /// <summary>
        /// Получение всех доступных ролей
        /// </summary>
        public async Task<List<UserRole>> GetAllRolesAsync()
        {
            try
            {
                var response = await _supabase.From<UserRole>().Get();
                return response.Models.OrderBy(r => r.Id).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки ролей: {ex.Message}");
                return new List<UserRole>();
            }
        }

        // --- ЗАМЕНИТЬ СТАРЫЙ МЕТОД ---
        public async Task<bool> UpdateEmployeeAsync(string userId, int? roleId, bool isActive)
        {
            try
            {
                // Чтобы избежать ошибки с null (Гость), мы сначала получаем профиль,
                // изменяем его свойства и отправляем целиком через .Update()
                var profileResponse = await _supabase.From<Profile>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, userId)
                    .Single();

                if (profileResponse == null) throw new Exception("Профиль не найден.");

                profileResponse.RoleId = roleId == null ? 0 : roleId.Value;
                profileResponse.IsActive = isActive;

                await _supabase.From<Profile>().Update(profileResponse);
                return true;
            }
            catch (Exception ex)
            {
                string errorMsg = ex.Message;
                if (errorMsg.Contains("Access Denied"))
                    throw new Exception("Отказано в доступе. Недостаточно прав для изменения этого профиля.");
                throw new Exception($"Ошибка при сохранении: {ex.Message}");
            }
        }

        // --- ДОБАВИТЬ НОВЫЕ МЕТОДЫ ---
        public async Task RenameRoleAsync(int roleId, string newName)
        {
            var role = await _supabase.From<UserRole>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, roleId).Single();
            if (role != null)
            {
                role.Name = newName;
                await _supabase.From<UserRole>().Update(role);
            }
        }

        public async Task DeleteRoleAsync(int roleId)
        {
            // Каскадное удаление (ON DELETE CASCADE) в БД само удалит все права и обнулит роли у юзеров
            await _supabase.From<UserRole>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, roleId).Delete();
        }
        // --- Добавить в AuthService.cs ---

        public async Task<List<Permission>> GetAllPermissionsAsync()
        {
            var response = await _supabase.From<Permission>().Get();
            return response.Models;
        }

        public async Task<List<int>> GetRolePermissionIdsAsync(int roleId)
        {
            // Получаем список ID разрешений для конкретной роли
            var response = await _supabase.From<Models.RolePermission>()
                .Where(x => x.RoleId == roleId)
                .Get();
            return response.Models.Select(x => x.PermissionId).ToList();
        }

        public async Task AddRoleAsync(string roleName)
        {
            var newRole = new UserRole { Name = roleName };
            await _supabase.From<UserRole>().Insert(newRole);
        }

        public async Task UpdateRolePermissionsAsync(int roleId, List<int> permissionIds)
        {
            // 1. Удаляем старые связки для этой роли
            await _supabase.From<Models.RolePermission>()
                .Where(x => x.RoleId == roleId)
                .Delete();

            // 2. Добавляем новые
            var newMappings = permissionIds.Select(pId => new Models.RolePermission
            {
                RoleId = roleId,
                PermissionId = pId
            }).ToList();

            if (newMappings.Any())
                await _supabase.From<Models.RolePermission>().Insert(newMappings);
        }

    }
}