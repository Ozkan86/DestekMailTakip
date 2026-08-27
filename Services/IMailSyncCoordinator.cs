namespace task_list.Services;

/// <summary>Bir senkronizasyon turunun sonucu (navbar'daki manuel butona geri bildirim icin).</summary>
/// <param name="Ran">Bu cagri gercekten bir tur calistirdi mi? Zaten devam eden bir senkronizasyon varsa false.</param>
/// <param name="Imported">Bu turda ice aktarilan yeni oge sayisi.</param>
/// <param name="Failed">Tur bir hatayla bitti mi (hata koordinatorde loglanir).</param>
public record MailSyncOutcome(bool Ran, int Imported, bool Failed);

/// <summary>
/// IMAP senkronizasyonunu istek yolundan ayirir ve uygulama genelinde tek noktadan
/// yonetir: ayni anda yalnizca bir senkronizasyon calisir, cok sik tetiklenirse
/// atlanir. Mail sayfasi artik senkronizasyonu BEKLEMEZ; sadece tetikler
/// (bkz. MailController.Index) ve yeni ogeler geldiginde <see cref="Version"/>
/// degistigi icin liste paneli kendini tazeler.
/// </summary>
public interface IMailSyncCoordinator
{
    /// <summary>Arka planda senkronizasyon tetikler ve hemen doner (beklemez).</summary>
    void RequestSync();

    /// <summary>
    /// Senkronizasyonu calistirir; zaten calisan bir senkronizasyon varsa atlar.
    /// Periyodik arka plan servisi bunu kullanir.
    /// </summary>
    Task<MailSyncOutcome> SyncNowAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Kullanicinin navbar'daki butonla elle tetikledigi senkronizasyon: sonucu
    /// bildirebilmek icin BEKLENIR ve <see cref="RequestSync"/>'teki bekleme
    /// suresine takilmaz (kullanici bilerek istedi). Istek iptal olsa bile tur
    /// yarida kesilmesin diye uygulama yasam dongusu token'i kullanilir.
    /// </summary>
    Task<MailSyncOutcome> SyncManuallyAsync();

    /// <summary>
    /// Her basarili ve en az bir yeni oge getiren senkronizasyondan sonra artar.
    /// Istemci bu degeri yoklayarak listeyi ne zaman tazeleyecegini anlar.
    /// </summary>
    int Version { get; }
}
