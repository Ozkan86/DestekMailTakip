using Microsoft.Extensions.Options;

namespace task_list.Services;

public class MailSyncBackgroundService : BackgroundService
{
    private readonly IMailSyncCoordinator _syncCoordinator;
    private readonly ImapSettings _settings;
    private readonly ILogger<MailSyncBackgroundService> _logger;

    public MailSyncBackgroundService(
        IMailSyncCoordinator syncCoordinator,
        IOptions<ImapSettings> settings,
        ILogger<MailSyncBackgroundService> logger)
    {
        _syncCoordinator = syncCoordinator;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(15, _settings.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                // Koordinator hatalari kendi icinde loglar ve yutar; ayrica ayni anda
                // baska bir senkronizasyon calisiyorsa bu turu atlar.
                await _syncCoordinator.SyncNowAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mail senkronizasyonu sirasinda hata olustu.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
