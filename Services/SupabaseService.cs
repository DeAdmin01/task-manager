using System;
using System.Collections.Generic;
using System.Text;
using Supabase;

namespace SmartArena.Services
{
    /// <summary>
    /// Основная служба для работы с БД
    /// </summary>
    public class SupabaseService
    {
        private const string _url = "https://lrmxhsgnssluizwooame.supabase.co";
        private const string _key = "sb_publishable_YvasGmgVZg02QWkZ_1oTSw_sXwK_5dG";

        public Supabase.Client Client { get; private set; }

        public SupabaseService()
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = true,
            };

            Client = new Supabase.Client(_url, _key, options);
        }

        public async Task InitializeAsync()
        {
            await Client.InitializeAsync();
        }
    }
}
