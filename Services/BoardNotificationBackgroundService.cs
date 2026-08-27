using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using task_list.Data;
using task_list.Models;

namespace task_list.Services;

/// <summary>
/// Pano bildirim kuyrugunu tuketir: e-postalari TEK bir SMTP baglantisi uzerinden,
/// uygulama-ici mesajlari da TEK bir DB baglantisi uzerinden toplu olarak gonderir.
/// Bu is istek yolundan tamamen ayrildigi icin kart ekleme/tasima uclari alici
/// sayisindan bagimsiz olarak hizli yanit doner.
/// </summary>
public class BoardNotificationBackgroundService : BackgroundService
{
    private readonly IBoardNotificationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BoardNotificationBackgroundService> _logger;

    public BoardNotificationBackgroundService(
        IBoardNotificationQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BoardNotificationBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Bildirim gonderimi basarisiz olsa bile uygulama akisi etkilenmemeli.
                _logger.LogError(ex, "Pano bildirimi işlenirken hata oluştu.");
            }
        }
    }

    private async Task ProcessAsync(BoardNotificationJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
        var mailSender = scope.ServiceProvider.GetRequiredService<IMailSenderService>();

        // 1) Tum mühendislere (Employee + Admin) uygulama-ici bildirim.
        if (!string.IsNullOrWhiteSpace(job.EngineerText))
        {
            var employees = await userManager.GetUsersInRoleAsync("Employee");
            var admins = await userManager.GetUsersInRoleAsync("Admin");

            var engineerMessages = employees.Concat(admins)
                .DistinctBy(u => u.Id)
                .Select(u => new MessageDispatch(null, "Sistem", job.EngineerText!, u.Id, u.DisplayName))
                .ToList();

            await messageRepository.SendMessagesAsync(engineerMessages);
        }

        // 2) Pano sahibi + yetkili e-posta adresleri: e-posta + (kayitliysa) uygulama-ici bildirim.
        if (string.IsNullOrWhiteSpace(job.AudienceText))
        {
            return;
        }

        var addresses = new List<string>();
        if (!string.IsNullOrWhiteSpace(job.OwnerUserId))
        {
            var owner = await userManager.FindByIdAsync(job.OwnerUserId);
            if (!string.IsNullOrWhiteSpace(owner?.Email))
            {
                addresses.Add(Normalize(owner.Email));
            }
        }
        addresses.AddRange(job.AuthorizedEmails.Select(Normalize));

        var targets = addresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal)
            .Where(a => string.IsNullOrEmpty(job.ExcludeEmail) || !string.Equals(a, job.ExcludeEmail, StringComparison.Ordinal))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        // SMTP erisilemezse (baglanti/kimlik hatasi) uygulama-ici bildirimler yine de
        // yazilmali; e-posta gonderimi bu isin geri kalanini iptal etmemeli.
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await mailSender.SendNotificationEmailsAsync(targets, job.EmailSubject, job.AudienceText!, cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation("Pano bildirim e-postaları gönderildi: {Count} alıcı, {ElapsedMs} ms (istek yolu dışında).",
                targets.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Pano bildirim e-postaları gönderilemedi ({Count} alıcı); uygulama-içi bildirimlere devam ediliyor.",
                targets.Count);
        }

        if (!job.NotifyRegisteredUsersInApp)
        {
            return;
        }

        // Kayitli hesaplari adres basina ayri sorgu yerine tek sorguda bul.
        var normalizedUpper = targets.Select(a => a.ToUpperInvariant()).ToList();
        var registeredUsers = await userManager.Users
            .Where(u => u.NormalizedEmail != null && normalizedUpper.Contains(u.NormalizedEmail))
            .ToListAsync(cancellationToken);

        var appMessages = registeredUsers
            .Select(u => new MessageDispatch(job.SenderUserId, job.SenderDisplayName, job.AudienceText!, u.Id, u.DisplayName))
            .ToList();

        await messageRepository.SendMessagesAsync(appMessages);
    }

    private static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
