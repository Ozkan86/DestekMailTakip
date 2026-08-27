namespace task_list.Services;

public class MailSyncCoordinator : IMailSyncCoordinator
{
    /// <summary>Istek uzerine tetiklenen senkronizasyonlar arasindaki en kisa sure.</summary>
    private static readonly TimeSpan OnDemandDebounce = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<MailSyncCoordinator> _logger;

    // Ayni anda tek senkronizasyon: hem istek uzerine tetiklenenler hem de periyodik
    // arka plan servisi bu kapidan gecer (Gmail eszamanli baglantiyi sinirlar ve ayni
    // taramanin paralel kopyalari bosuna is uretir).
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _lastCompletedAt = DateTimeOffset.MinValue;
    private int _version;

    public MailSyncCoordinator(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        ILogger<MailSyncCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _logger = logger;
    }

    public int Version => Volatile.Read(ref _version);

    public void RequestSync()
    {
        if (DateTimeOffset.UtcNow - _lastCompletedAt < OnDemandDebounce)
        {
            return;
        }

        // Istek yolunu bloklamamak icin kasitli olarak beklenmiyor; hatalar
        // SyncNowAsync icinde loglanip yutuluyor.
        _ = Task.Run(() => SyncNowAsync(_lifetime.ApplicationStopping));
    }

    /// <summary>
    /// Navbar'daki manuel senkronizasyon butonu. Istegin iptali (kullanici sayfadan
    /// ayrilirsa) turu yarida kesmesin diye uygulama yasam dongusu token'i verilir.
    /// </summary>
    public Task<MailSyncOutcome> SyncManuallyAsync() => SyncNowAsync(_lifetime.ApplicationStopping);

    public async Task<MailSyncOutcome> SyncNowAsync(CancellationToken cancellationToken)
    {
        // Devam eden bir senkronizasyon varsa bu turu atla (is zaten yapiliyor).
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return new MailSyncOutcome(Ran: false, Imported: 0, Failed: false);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var imapService = scope.ServiceProvider.GetRequiredService<IImapMailService>();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var imported = await imapService.SyncAsync(cancellationToken);
            stopwatch.Stop();

            if (imported > 0)
            {
                Interlocked.Increment(ref _version);
            }

            _logger.LogInformation("Mail senkronizasyonu tamamlandı: {Count} yeni öğe, {ElapsedMs} ms (istek yolu dışında).",
                imported, stopwatch.ElapsedMilliseconds);

            return new MailSyncOutcome(Ran: true, Imported: imported, Failed: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Uygulama kapaniyor; sessizce cik.
            return new MailSyncOutcome(Ran: false, Imported: 0, Failed: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mail senkronizasyonu sırasında hata oluştu.");
            return new MailSyncOutcome(Ran: true, Imported: 0, Failed: true);
        }
        finally
        {
            _lastCompletedAt = DateTimeOffset.UtcNow;
            _gate.Release();
        }
    }
}
