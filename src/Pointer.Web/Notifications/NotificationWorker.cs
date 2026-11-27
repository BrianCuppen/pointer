using Pointer.Core.Notifications;

namespace Pointer.Web.Notifications;

/// <summary>Background loop: queue due reminders and overviews, then send what is queued.</summary>
public sealed class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var scheduler = scope.ServiceProvider.GetRequiredService<NotificationScheduler>();
                await scheduler.EnqueueDueRemindersAsync(stoppingToken);
                await scheduler.EnqueueDailyOverviewsAsync(stoppingToken);

                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                while (await dispatcher.DispatchDueAsync(20, stoppingToken) == 20) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification run failed; retrying next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
