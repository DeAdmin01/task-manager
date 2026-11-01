using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using Plugin.LocalNotification.AndroidOption;
using Plugin.LocalNotification.Core.Models.AndroidOption;
using SmartArena.Interfaces;
using SmartArena.Services;

namespace SmartArena
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseLocalNotification(config =>
                {
#if ANDROID
                    config.AddAndroid(android =>
                    {
                        android.AddChannel(new AndroidNotificationChannelRequest
                        {
                            Id = "smartarena_high_alerts",
                            Name = "Срочные уведомления стадиона",
                            Description = "Канал для критически важных заявок",
                            Importance = AndroidImportance.High, // Используем AndroidImportance
                            LockScreenVisibility = AndroidVisibilityType.Public // Видно на экране блокировки
                        });
                    });
#endif
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddSingleton<SupabaseService>(); // Главный клиент
            builder.Services.AddSingleton<AuthService>();     // Логин/Логаут
            builder.Services.AddSingleton<ChatService>();     // Чаты
            builder.Services.AddSingleton<CryptoService>();     // Криптография
            builder.Services.AddScoped<IAlertService, BlazorAlertService>();
            builder.Services.AddSingleton<ISystemNotificationService, SystemNotificationService>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
