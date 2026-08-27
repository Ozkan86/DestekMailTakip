using task_list.Models;

namespace task_list.Data;

public interface IMailRepository
{
    Task<List<MailMessageModel>> GetByFolderAsync(string folder, string currentUserId, bool onlyMine = false);

    /// <summary>
    /// Bir klasordeki mailerin Id'lerini, listede goruntulendikleri sirayla doner
    /// (detay sayfasindaki onceki/sonraki gezinmesi icin, tum modeli hidratlamadan).
    /// </summary>
    Task<List<int>> GetOrderedMailIdsAsync(string folder, string currentUserId, bool onlyMine = false);

    Task<List<int>> GetCustomFolderOrderedMailIdsAsync(int folderId, string userId);

    Task<Dictionary<string, int>> GetFolderCountsAsync(string currentUserId);
    Task<MailMessageModel?> GetByIdAsync(int id);
    Task MarkAsReadAsync(int id);

    Task<HashSet<string>> GetExistingImapUidsAsync();
    Task<int> InsertMailAsync(MailMessageModel mail);
    Task InsertAttachmentAsync(int mailId, MailAttachment attachment);
    Task UpdateMailBodyHtmlAsync(int mailId, string bodyHtml);
    Task MarkAsUnreadAsync(int id);

    /// <summary>
    /// Var olan tum mailerin MessageId -> Id eslesmesini doner (IMAP thread eslestirmesi icin).
    /// </summary>
    Task<Dictionary<string, int>> GetMessageIdToMailIdMapAsync();

    /// <summary>
    /// Musteriden gelen (In-Reply-To/References ile eslesen) bir takip mesajini,
    /// var olan mail'e bagli gelen yanit olarak ekler.
    /// </summary>
    Task<int> InsertInboundReplyAsync(MailReply reply);

    /// <summary>
    /// Mail'in bayragini degistirir (Active/Pending/Closed/Spam - MailFlagTypes).
    /// Bayrak bos ise (FlagSetByUserId NULL) veya cagiran kullaniciya aitse degisir ve
    /// yeni sahibi cagiran kullanici olur; baskasina aitse LockedByOtherUser doner.
    /// Ayrica MailFlagPolicy kurallari uygulanir (atanmamis mail tamamlanamaz, gorev
    /// ustunde olmayan muhendis beklemede/tamamlandi/spam isaretleyemez) -> NotAllowed.
    /// </summary>
    Task<MailFlagUpdateResult> SetFlagAsync(int mailId, string flagType, string userId, string userDisplayName);

    Task<int> InsertReplyAsync(MailReply reply);
    Task InsertReplyAttachmentAsync(int replyId, MailReplyAttachment attachment);

    /// <summary>
    /// Verilen kullanicilari maile atar (zaten atanmis olanlar atlanir, coklu atamaya izin verir).
    /// taskName doluysa mailin TaskName'i guncellenir (bossa mevcut deger korunur).
    /// </summary>
    Task AssignUsersAsync(int mailId, IEnumerable<string> userIds, string? actingUserId, string? actingUserDisplayName, string? taskName = null);

    /// <summary>
    /// Tek bir kullanicinin atamasini kaldirir. Basarisiz olursa false doner (atama zaten yoksa).
    /// </summary>
    Task<bool> RemoveAssignmentAsync(int mailId, string userId);

    /// <summary>
    /// Tek renkli kose belirtecini ayarlar/kaldirir (colorKey null ise kaldirilir).
    /// </summary>
    Task SetCategoryMarkerAsync(int mailId, string? colorKey);

    /// <summary>
    /// Mail'in tum etiketlerini verilen setle degistirir (var olanlar silinir, yenileri eklenir).
    /// </summary>
    Task SetTagsAsync(int mailId, IEnumerable<(string Text, string ColorKey)> tags, string? userId);

    Task SaveDraftAsync(int mailId, string body, string userId);
    Task ClearDraftAsync(int mailId);

    Task DeleteMailAsync(int mailId);

    Task<List<MailDraftTemplate>> GetDraftTemplatesAsync(string currentUserId);
    Task<int> CreateDraftTemplateAsync(string title, string body, string userId, bool isPrivate);
    Task UpdateDraftTemplateAsync(int id, string title, string body, string userId, bool isPrivate);
    Task DeleteDraftTemplateAsync(int id);

    /// <summary>
    /// Mail govdesi uzerine kullaniciya ozel cizim/isaretleme verisini (JSON) doner. Yoksa null.
    /// </summary>
    Task<string?> GetAnnotationsAsync(int mailId, string userId);

    /// <summary>
    /// Kullaniciya ozel cizim/isaretleme verisini kaydeder (upsert).
    /// </summary>
    Task SaveAnnotationsAsync(int mailId, string userId, string strokesJson);

    /// <summary>
    /// Bir kullanicinin kendi hesabina ozel klasorlerini (sayaclariyla) doner.
    /// </summary>
    Task<List<MailUserFolder>> GetUserFoldersAsync(string userId);

    Task<MailUserFolder?> GetUserFolderAsync(int folderId, string userId);
    Task<int> CreateUserFolderAsync(string userId, string name);
    Task DeleteUserFolderAsync(int folderId, string userId);

    /// <summary>
    /// Kullaniciya ozel bir klasordeki mailleri doner (klasorun gercekten bu kullaniciya ait oldugu dogrulanir).
    /// </summary>
    Task<List<MailMessageModel>> GetByCustomFolderAsync(int folderId, string userId);

    /// <summary>
    /// Bu mailin, verilen kullanicinin hangi klasorlerinde (Id) yer aldigini doner.
    /// </summary>
    Task<HashSet<int>> GetMailFolderIdsAsync(int mailId, string userId);

    /// <summary>
    /// Bu mail icin, kullanicinin klasor uyeligini verilen sete esitler (etiket gibi; diger klasorleri etkilemez).
    /// </summary>
    Task SetMailFoldersAsync(int mailId, string userId, IEnumerable<int> folderIds);

    Task<bool> IsArchivedAsync(int mailId, string userId);
    Task SetArchivedAsync(int mailId, string userId, bool archived);

    /// <summary>
    /// Kullanicinin bu maili "en son ne zaman gordugunu" gunceller (upsert); sadece
    /// kullanici bu maile daha once en az bir kez kendi yaniti varsa anlamlidir
    /// ("Görevlerime Ait Mesajlar" istatistigi icin, bkz. IStatsRepository).
    /// </summary>
    Task UpsertMailReplySeenAsync(int mailId, string userId);
}
