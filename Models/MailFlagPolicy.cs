namespace task_list.Models;

/// <summary>
/// Bir bayragin (Aktif/Beklemede/Tamamlandi/Spam) o an uygulanip uygulanamayacagini
/// belirleyen TEK kural noktasi. Hem arayuz (secenegi inaktif gosterip ipucu yazmak icin)
/// hem de sunucu tarafi (MailRepository.SetFlagAsync) ayni metotlari kullanir; boylece
/// dogrudan POST atilarak kural atlatilamaz.
///
/// Kurallar:
///  1) Mail henuz hicbir muhendise atanmamissa (Atanmamis klasoru) "Tamamlandi"
///     isaretlenemez: kimsenin ustlenmedigi bir gorev tamamlanmis olamaz.
///  2) Mail atanmis ama isleyen kullanici atananlar arasinda degilse; "Beklemede",
///     "Tamamlandi" ve "Spam" isaretlenemez: baskasinin ustundeki gorevin durumunu
///     degistiremez. "Aktif" serbesttir (hatali bir bayragi geri almak icin).
///
/// Bunlarin uzerine, MailRepository.SetFlagAsync'teki mevcut "bayragi sadece koyan
/// kisi degistirebilir" kilidi de ayrica islemeye devam eder.
/// </summary>
public static class MailFlagPolicy
{
    public const string UnassignedClosedMessage =
        "Bu klasördeki mailler henüz bir mühendise atanmadığı için tamamlandı olarak işaretlenemez.";

    public const string NotAssigneeMessage =
        "Bu görev sizin üstünüzde olmadığı için bu işaretlemeyi yapamazsınız.";

    public const string OwnerLockMessage =
        "Bu bayrağı başka bir kullanıcı koydu; yalnızca görevin atandığı mühendisler değiştirebilir.";

    /// <summary>
    /// Bayrak uygulanamiyorsa kullaniciya gosterilecek nedeni, uygulanabiliyorsa null doner.
    /// </summary>
    public static string? DenyReason(string flagType, bool hasAssignment, bool isAssignedToCurrentUser)
    {
        if (!hasAssignment)
        {
            return flagType == MailFlagTypes.Closed ? UnassignedClosedMessage : null;
        }

        if (isAssignedToCurrentUser)
        {
            return null;
        }

        return flagType is MailFlagTypes.Pending or MailFlagTypes.Closed or MailFlagTypes.Spam
            ? NotAssigneeMessage
            : null;
    }

    public static bool IsAllowed(string flagType, bool hasAssignment, bool isAssignedToCurrentUser) =>
        DenyReason(flagType, hasAssignment, isAssignedToCurrentUser) is null;

    /// <summary>
    /// "Bayragi sadece koyan kisi degistirebilir" kilidi, atama modeline TABIDIR:
    /// bayrak bir gorevin durumudur, kisisel bir isaret degil. Bu yuzden kilit
    /// yalnizca gorev BASKASININ ustundeyken (mail atanmis ve isleyen kullanici
    /// atananlar arasinda degilken) calisir.
    ///
    /// Boylece:
    ///  - Ayni goreve atanmis iki muhendisten biri, digerinin koydugu bayragi
    ///    degistirebilir (gorev ikisinin de ustunde).
    ///  - Henuz kimseye atanmamis bir mailde kimse bayragin sahibi sayilmaz;
    ///    biri "Beklemede" birakti diye mail digerlerine kilitlenmez.
    /// </summary>
    public static bool IsOwnerLockActive(
        string? flagSetByUserId,
        string? currentUserId,
        bool hasAssignment,
        bool isAssignedToCurrentUser)
    {
        if (string.IsNullOrEmpty(flagSetByUserId) ||
            string.Equals(flagSetByUserId, currentUserId, StringComparison.Ordinal))
        {
            return false;
        }

        return hasAssignment && !isAssignedToCurrentUser;
    }
}

/// <summary>
/// SetFlagAsync sonucunun ayrintili hali: cagiran taraf kullaniciya dogru
/// uyari metnini gosterebilsin diye "neden basarisiz oldu" bilgisini tasir.
/// </summary>
public enum MailFlagUpdateResult
{
    Success,

    /// <summary>Mail bulunamadi.</summary>
    NotFound,

    /// <summary>Bayragi baska bir kullanici koymus; sadece o degistirebilir.</summary>
    LockedByOtherUser,

    /// <summary>Mail hicbir muhendise atanmadigi icin "Tamamlandi" isaretlenemedi.</summary>
    NotAllowedUnassigned,

    /// <summary>Gorev isleyen kullanicinin ustunde olmadigi icin isaretlenemedi.</summary>
    NotAllowedNotAssignee
}
