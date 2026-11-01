using SmartArena.Services;
using Supabase.Postgrest.Interfaces;
using Supabase.Postgrest.Models;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.PostgresChanges;
using System;
using System.Collections.Generic;
using System.Text;

namespace SmartArena.Classes
{
    public abstract class BaseRepository<T> where T : BaseModel, new()
    {
        protected readonly Supabase.Client _supabase;

        protected BaseRepository(SupabaseService service)
        {
            _supabase = service.Client;
        }

        // Получить список всех записей
        public async Task<List<T>> GetAll()
        {
            var result = await _supabase.From<T>().Get();
            return result.Models;
        }

        // Получить одну запись по ID
        public async Task<T?> GetById(string id)
        {
            // 1. Выполняем запрос. Фильтруем по "id" и ожидаем одну запись (.Single())
            // Используем явную фильтрацию через Equals
            var response = await _supabase
                .From<T>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Single();

            // 2. Возвращаем Model из ответа (Response)
            // response — это объект типа ModeledResponse<T>, у которого есть свойство Model
            return response;
        }

        // Вставить новую запись
        public async Task<T> Insert(T model)
        {
            var result = await _supabase.From<T>().Insert(model);
            return result.Model;
        }

        // Подписка на изменения в таблице (например, для обновления UI в реальном времени)
        public void SubscribeToChanges(PostgresChangesOptions.ListenType eventType, Action<T> callback)
        {
            _supabase.From<T>().On(eventType, (IRealtimeChannel channel, PostgresChangesResponse change) =>
            {
                var model = change.Model<T>();
                if (model != null)
                {
                    callback(model);
                }
            });
        }
        // Обновление записи (например, смена статуса тикета)
        public async Task<T> Update(T model)
        {
            var result = await _supabase.From<T>().Update(model);
            return result.Model;
        }

        // Удаление записи
        public async Task Delete(T model)
        {
            await _supabase.From<T>().Delete(model);
        }

        // Удаление по ID (удобная обертка)
        public async Task DeleteById(string id)
        {
            await _supabase.From<T>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Delete();
        }
    }
}
