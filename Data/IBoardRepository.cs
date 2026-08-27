using task_list.Models;

namespace task_list.Data;

public interface IBoardRepository
{
    Task<int> CreateBoardAsync(string title, string ownerId, string ownerDisplayName, IEnumerable<string> authorizedEmails, string templateKey);

    /// <summary>
    /// Musterinin "Pano Sablonlarim" bolumunden baslattigi gecici sablon
    /// onizlemesi: ayni (userId, templateKey) icin var olan eski onizleme
    /// panosunu (varsa) siler, yeni bir IsPreview=1 pano olusturur ve
    /// BoardTemplatePreviewSeeds'teki 3'er ornek karti her listeye ekler.
    /// </summary>
    Task<int> StartPreviewBoardAsync(string templateKey, string userId, string userDisplayName);

    /// <summary>
    /// Onizleme panosunu siler; boardId, IsPreview=1 ve CreatedByUserId=userId
    /// ile eslesmiyorsa hicbir sey yapmaz (no-op).
    /// </summary>
    Task DeletePreviewBoardAsync(int boardId, string userId);

    /// <summary>Panonun sablon anahtarini tek basina, hafif bir sorguyla doner.</summary>
    Task<string?> GetBoardTemplateKeyAsync(int boardId);

    /// <summary>
    /// Musterinin sahip oldugu veya e-postasiyla yetkilendirildigi panolar.
    /// </summary>
    Task<List<Board>> GetBoardsForCustomerAsync(string userId, string normalizedEmail);

    /// <summary>
    /// Musterinin "Panolarım" sayfasindaki ozet widget'i icin: toplam pano,
    /// acik kart ve onay bekleyen kart sayilari ile son birkac kart aktivitesi.
    /// </summary>
    Task<CustomerBoardSummary> GetCustomerBoardSummaryAsync(string userId, string normalizedEmail);

    /// <summary>
    /// Muhendis/Admin icin tum panolarin global listesi.
    /// </summary>
    Task<List<Board>> GetAllBoardsAsync();

    /// <summary>
    /// Pano + yetkili e-postalar + tum kartlari (detay sayfasi icin) doner.
    /// </summary>
    Task<Board?> GetBoardDetailsAsync(int boardId);

    Task<bool> IsCustomerAuthorizedAsync(int boardId, string userId, string normalizedEmail);

    /// <summary>
    /// Panoyu ve tum kartlarini/etiketlerini/yetkili e-postalarini siler
    /// (veritabaninda ON DELETE CASCADE ile).
    /// </summary>
    Task DeleteBoardAsync(int boardId);

    Task RenameBoardAsync(int boardId, string title);

    /// <summary>
    /// Panoya yeni yetkili musteri e-postalari ekler; halihazirda yetkili
    /// olanlar (buyuk/kucuk harf gozetmeksizin) yeniden eklenmez.
    /// </summary>
    Task AddAuthorizedEmailsAsync(int boardId, IEnumerable<string> emails);

    /// <summary>
    /// Panodan bir yetkili musteri e-postasini kaldirir.
    /// </summary>
    Task RemoveAuthorizedEmailAsync(int boardId, string email);

    Task SetListColorAsync(int boardId, string listKey, string? color);

    /// <summary>
    /// Klasik disi sablonlar icin: (BoardId, ListKey) bazinda kolon arkaplan
    /// rengini kaydeder; color null ise satiri siler (varsayilana doner).
    /// </summary>
    Task SetGenericListColorAsync(int boardId, string listKey, string? color);

    /// <summary>
    /// Bu kartın kendi etiketlerini döner (etiketler karta özgüdür, panodaki başka
    /// kartlarla paylaşılmaz). Kartın hiç etiketi yoksa 3 varsayılan (isimsiz
    /// sarı/mor/mavi) etiket bu kart için seed edilip döndürülür.
    /// </summary>
    /// <summary>Kartin uzerinde gorunen (secili) etiketleri.</summary>
    Task<List<BoardLabel>> GetLabelsForCardAsync(int boardId, int cardId);

    /// <summary>
    /// "Etiketleri Düzenle" ekraninin listesi: secili/secisiz tum etiketler; eksik
    /// varsayilan renkler secilmemis olarak tamamlanir.
    /// </summary>
    Task<List<BoardLabel>> GetCardLabelPaletteAsync(int boardId, int cardId);

    /// <summary>Etiketin kartta gorunup gorunmeyecegini belirler (kutucuk).</summary>
    Task<bool> SetLabelSelectedAsync(int labelId, int cardId, bool isSelected, string? actorUserId = null);

    Task<BoardLabel?> GetLabelByIdAsync(int labelId);

    /// <summary>Bu karta özgü yeni bir etiket oluşturur.</summary>
    Task<BoardLabel> CreateLabelForCardAsync(int boardId, int cardId, string name, string color, string? actorUserId);

    /// <summary>Sadece verilen kartın kendi etiketiyse günceller (başka bir karta ait etikete dokunmaz); başarılıysa true döner.</summary>
    Task<bool> UpdateLabelAsync(int labelId, int cardId, string name, string color, string? actorUserId = null);

    /// <summary>Sadece verilen kartın kendi etiketiyse siler (başka bir karta ait etikete dokunmaz); başarılıysa true döner.</summary>
    Task<bool> DeleteLabelAsync(int labelId, int cardId);

    Task<int> AddCardAsync(int boardId, string title, string? description, string creatorId, string creatorDisplayName);

    /// <summary>
    /// Klasik disi sablonlar icin: acikca belirtilen listeye kart ekler ve
    /// (sablon sprint turu kullaniyorsa) panonun guncel turunu karta damgalar.
    /// </summary>
    Task<int> AddCardToListAsync(int boardId, string listKey, string title, string? description, string creatorId, string creatorDisplayName);

    /// <summary>
    /// Klasik disi sablonlar icin jenerik, gecis-kurali dogrulamali kart tasima.
    /// Mühendis rolündeki bir geçiş için kart once actingUserId tarafindan
    /// "üstüne alinmis" (AssignedUserId) olmali; Admin, atanmis olmasi sartiyla
    /// (kendisine atanmis olmasi sart olmaksizin) tasiyabilir.
    /// </summary>
    Task<BoardMoveResult> MoveCardWithTransitionAsync(int cardId, int boardId, string targetListKey, string actingUserId, bool isEngineer, bool isAdmin, string? note);

    /// <summary>
    /// "Software Development" sablonunda Sprint Done listesindeki bir karta
    /// tek tek Onaylandi/Reddedildi durumu atar.
    /// </summary>
    Task SetCardApprovalStatusAsync(int cardId, string status, string actorUserId);

    /// <summary>
    /// "Software Development" sablonu icin: mevcut sprint turunun tamamlanma
    /// kosullari saglaniyorsa panonun CurrentSprintRound'unu bir arttirir.
    /// </summary>
    Task<bool> TryAdvanceSprintRoundAsync(int boardId);

    /// <summary>
    /// Bir mühendisin bir karti "üstüne almasi" (Klasik'te sadece Yapilacaklar
    /// listesinde kullanilir; jenerik sablonlarda mühendis rolündeki bir gecisin
    /// kaynak listesinde oldugu surece herhangi bir listede kullanilabilir -
    /// liste uygunlugu controller tarafinda sablona gore dogrulanir). Basarili
    /// olursa (kart kimseye atanmamissa) true doner.
    /// </summary>
    Task<bool> AssignCardToSelfAsync(int cardId, string userId, string userDisplayName);

    /// <summary>
    /// AssignCardToSelfAsync'in tersi: sadece kart hala uzerine alindigi
    /// listedeyken (ListKey = AssignedListKey) ve cagiran kullaniciya
    /// atanmisken basarili olur (true doner).
    /// </summary>
    Task<bool> ReleaseCardAssignmentAsync(int cardId, string userId);

    /// <summary>
    /// Basarili olursa true doner (kart, cagiran kullaniciya atanmissa).
    /// </summary>
    Task<bool> MoveCardToTestAsync(int cardId, string actingUserId, bool isAdmin);

    Task ApproveCardAsync(int cardId, string actorUserId);

    Task RejectCardAsync(int cardId, string note, string actorUserId);

    Task<BoardCard?> GetCardByIdAsync(int cardId);

    /// <summary>
    /// Karti kalici olarak siler (etiket atamalari/ekler/yorumlar ON DELETE
    /// CASCADE ile birlikte gider). Sadece BoardController.CanDeleteCardAsync
    /// tarafindan yetkilendirilmis (kartin bulundugu listede kart ekleme
    /// yetkisi olan) cagrilardan kullanilir.
    /// </summary>
    Task DeleteCardAsync(int cardId);

    /// <summary>
    /// "Pano Gorevlerim": kullaniciya atanmis, tum panolardaki (sablon farketmeksizin)
    /// arsivlenmemis kartlar; her kartin ait oldugu panonun sablon anahtari da doner
    /// (kartin hangi listeye tasinabilecegini hesaplamak icin controller'da kullanilir).
    /// </summary>
    Task<List<(BoardCard Card, string TemplateKey)>> GetMyAssignedTasksAsync(string userId);

    Task SetCardCoverColorAsync(int cardId, string color);
    Task SetCardCoverImageAsync(int cardId, string imagePath);
    Task ClearCardCoverAsync(int cardId);

    /// <summary>
    /// Panonun her listesindeki (todo/test/done) mevcut kart sayisi.
    /// </summary>
    Task<Dictionary<string, int>> GetCardCountByListAsync(int boardId);

    /// <summary>
    /// Karti (gerekirse baska bir listeye) hedef 1-tabanli konuma tasir; hem
    /// kaynak hem hedef listedeki diger kartlarin SortOrder'i buna gore kaydirilir.
    /// Klasik panoda liste degistiren hareketler (surukle-birak, "Teste Taşı"
    /// gibi butonlar) bunu kullanir.
    /// </summary>
    Task MoveCardAsync(int cardId, int boardId, string targetListKey, int targetPosition);

    /// <summary>
    /// Karti arsivler (panodan gizlenir); sadece arsivleyen kullanicinin
    /// Arsiv Kutusu'nda gorunur.
    /// </summary>
    Task ArchiveCardAsync(int cardId, string userId);

    /// <summary>
    /// Arsivlenmis karti, bulundugu listenin sonuna ekleyerek geri getirir.
    /// </summary>
    Task RestoreCardAsync(int cardId);

    /// <summary>
    /// Belirli bir kullanicinin arsivledigi kartlari (en yeni once) doner.
    /// </summary>
    Task<List<BoardCard>> GetArchivedCardsForUserAsync(string userId);

    Task UpdateCardDescriptionAsync(int cardId, string? descriptionHtml);

    Task<BoardCardAttachment> AddCardAttachmentLinkAsync(int cardId, string url, string userId, string displayName);
    Task<BoardCardAttachment> AddCardAttachmentFileAsync(int cardId, string filePath, string fileName, string userId, string displayName);
    Task<List<BoardCardAttachment>> GetAttachmentsForCardAsync(int cardId);
    Task<BoardCardAttachment?> GetAttachmentByIdAsync(int attachmentId);
    Task DeleteCardAttachmentAsync(int attachmentId);

    Task<BoardCardComment> AddCardCommentAsync(int cardId, string userId, string displayName, string role, string bodyHtml);
    Task<List<BoardCardComment>> GetCommentsForCardAsync(int cardId);
    Task<BoardCardComment?> GetCommentByIdAsync(int commentId);
    Task UpdateCardCommentAsync(int commentId, string bodyHtml);

    /// <summary>
    /// WhatsApp benzeri tepki: kullanicinin o yorumdaki tek tepkisi ayni emoji ise
    /// kaldirilir, farkli emoji ise degistirilir, hic yoksa eklenir.
    /// </summary>
    Task ToggleCommentReactionAsync(int commentId, string userId, string displayName, string emoji);
    Task<List<BoardCardCommentReaction>> GetReactionsForCommentAsync(int commentId);
    Task<List<BoardCardCommentReaction>> GetReactionsForCardAsync(int cardId);
}
