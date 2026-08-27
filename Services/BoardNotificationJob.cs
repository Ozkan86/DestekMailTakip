namespace task_list.Services;

/// <summary>
/// Bir pano olayinin (kart eklendi/tasindi/onaylandi, panoya yetki verildi...)
/// bildirim isi. Istek yolunda sadece kuyruga birakilir; SMTP gonderimi ve
/// uygulama-ici mesaj yazimi <see cref="BoardNotificationBackgroundService"/>
/// tarafindan arka planda yapilir. Boylece kart ekleme/tasima uclari, alici
/// sayisindan bagimsiz olarak aninda yanit doner.
/// </summary>
public class BoardNotificationJob
{
    /// <summary>Doluysa, tum mühendislere (Employee + Admin) uygulama-ici mesaj gonderilir.</summary>
    public string? EngineerText { get; init; }

    /// <summary>Doluysa, asagidaki adres kitlesine e-posta + (kayitliysa) uygulama-ici mesaj gonderilir.</summary>
    public string? AudienceText { get; init; }

    /// <summary>Pano sahibi; e-posta adresi arka planda cozulur (istek yolunda sorgu yapilmaz).</summary>
    public string? OwnerUserId { get; init; }

    /// <summary>Panonun yetkili e-posta adresleri.</summary>
    public IReadOnlyList<string> AuthorizedEmails { get; init; } = Array.Empty<string>();

    /// <summary>Bu adrese gonderilmez (genellikle islemi yapan kisinin kendisi).</summary>
    public string? ExcludeEmail { get; init; }

    public string EmailSubject { get; init; } = string.Empty;

    public string? SenderUserId { get; init; }

    public string SenderDisplayName { get; init; } = "Sistem";

    /// <summary>
    /// Adres kitlesindeki kayitli hesaplara e-postaya ek olarak uygulama-ici bildirim
    /// de yazilsin mi? Pano olay bildirimlerinde true; sisteme henuz kayitli olmayan
    /// kisilere gonderilen davet e-postalarinda false (eski davranisla ayni).
    /// </summary>
    public bool NotifyRegisteredUsersInApp { get; init; } = true;
}
