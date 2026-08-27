using task_list.Models;

namespace task_list.Services;

/// <summary>
/// Kullanici rozetlerinin (avatar) rengini tek noktadan yonetir. Renk, kullanici
/// kaydinda saklanan tekil indeksten okunur; boylece uygulamanin rozet kullanan
/// her noktasinda ayni mühendis ayni renkle gorunur. Palet 8 renkten olustugu
/// icin 9. kullanicidan itibaren renkler tekrar kullanilir (bkz. AvatarPalette).
/// </summary>
public interface IUserAvatarColorService
{
    /// <summary>Kullanicinin kalici renk indeksi (yoksa atanip kaydedilir).</summary>
    int ColorIndexFor(string? userId);

    /// <summary>Kullanicinin rozet rengi ("#rrggbb"). userId bos ise isim tabanli renk doner.</summary>
    string ColorFor(string? userId, string? fallbackSeed = null);

    /// <summary>Yeni olusturulan kullaniciya bos olan en kucuk indeksi atar ve kaydeder.</summary>
    Task<int> AssignColorIndexAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>Indeksi olmayan tum kullanicilara tekil indeks atar (uygulama acilisinda).</summary>
    Task BackfillAsync(CancellationToken cancellationToken = default);
}
