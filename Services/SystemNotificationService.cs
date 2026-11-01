using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using SmartArena.Interfaces;
using Plugin.LocalNotification.Core.Models.AndroidOption;



#if ANDROID
using Plugin.LocalNotification.AndroidOption;
using Android.App;
#endif

namespace SmartArena.Interfaces
{
    public interface ISystemNotificationService
    {
        Task ShowLocalNotificationAsync(string title, string description, int badgeNumber = 0);
    }
}

namespace SmartArena.Services
{
    public class SystemNotificationService : ISystemNotificationService
    {
        private const string HighPriorityChannelId = "smartarena_high_alerts";

        public async Task ShowLocalNotificationAsync(string title, string description, int badgeNumber = 0)
        {
            var notificationId = new Random().Next(10000, 99999);

            var request = new NotificationRequest
            {
                NotificationId = notificationId,
                Title = title,
                Description = description,
                BadgeNumber = badgeNumber,
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = DateTime.Now
                },
#if ANDROID
                // Конфигурация для Android: привязываем пуш к созданному каналу
                Android = new AndroidOptions
                {
                    ChannelId = HighPriorityChannelId,
                    Priority = AndroidPriority.High,
                    // Исправлено: теперь используем правильный тип из плагина
                    VisibilityType = AndroidVisibilityType.Public
                }
#endif
            };

            await LocalNotificationCenter.Current.Show(request);
        }
    }
}