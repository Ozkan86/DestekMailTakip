using System.ComponentModel.DataAnnotations;

namespace task_list.Models;

public class Board
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public List<string> AuthorizedEmails { get; set; } = new();
    public List<BoardCard> Cards { get; set; } = new();

    public string? TodoColor { get; set; }
    public string? TestColor { get; set; }
    public string? DoneColor { get; set; }

    public string TemplateKey { get; set; } = BoardTemplates.Klasik;
    public int CurrentSprintRound { get; set; } = 1;

    /// <summary>
    /// Musterinin "Pano Sablonlarim" bolumunden baslattigi, gecici bir sablon
    /// onizlemesi mi (bkz. BoardTemplatePreviewSeeds, BoardController.StartPreview/StopPreview).
    /// Boyle panolar normal pano listelerinde/istatistiklerde/bildirimlerde gorunmez.
    /// </summary>
    public bool IsPreview { get; set; }

    /// <summary>
    /// Klasik disi sablonlarda kolon arkaplan renkleri (liste sayisi/anahtari
    /// degisken oldugu icin TodoColor/TestColor/DoneColor gibi sabit kolonlar
    /// yerine anahtar-deger olarak tutulur; sadece renk secilmis listeler icin girdi vardir).
    /// </summary>
    public Dictionary<string, string> ListColors { get; set; } = new();

    public BoardTemplateDefinition Template => BoardTemplates.Get(TemplateKey);

    public string? GetGenericListColor(string listKey) =>
        ListColors.TryGetValue(listKey, out var color) ? color : null;

    public string? GetListColor(string listKey) => listKey switch
    {
        BoardLists.Todo => TodoColor,
        BoardLists.Test => TestColor,
        BoardLists.Done => DoneColor,
        _ => null
    };

    public List<BoardCard> TodoCards => Cards.Where(c => c.ListKey == BoardLists.Todo).ToList();
    public List<BoardCard> TestCards => Cards.Where(c => c.ListKey == BoardLists.Test).ToList();
    public List<BoardCard> DoneCards => Cards.Where(c => c.ListKey == BoardLists.Done).ToList();
}

public class BoardCard
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string BoardTitle { get; set; } = string.Empty;
    public string ListKey { get; set; } = BoardLists.Todo;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public string? AssignedUserId { get; set; }
    public string? AssignedUserDisplayName { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    /// <summary>
    /// Kartin uzerine alindigi andaki ListKey'i (bkz. AssignCardToSelfAsync).
    /// "Üstümden Bırak" (ReleaseFromMe) sadece kart hala bu listedeyken
    /// mumkundur; mühendis karti sonraki bir listeye tasidiktan sonra
    /// ListKey != AssignedListKey oldugu icin birakma secenegi devre disi kalir.
    /// </summary>
    public string? AssignedListKey { get; set; }

    public DateTimeOffset? MovedToTestAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public string? LastRejectionNote { get; set; }
    public DateTimeOffset? LastRejectedAt { get; set; }
    public int RejectedCount { get; set; }

    public string? CoverColor { get; set; }
    public string? CoverImagePath { get; set; }
    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ArchivedByUserId { get; set; }

    public int SprintRound { get; set; } = 1;
    public string? ApprovalStatus { get; set; }

    public List<BoardLabel> Labels { get; set; } = new();

    public bool IsAssigned => !string.IsNullOrEmpty(AssignedUserId);
    public bool HasCover => !string.IsNullOrEmpty(CoverColor) || !string.IsNullOrEmpty(CoverImagePath);

    // Kart onizlemesinde (pano kolonunda) Aciklama artik zengin metin HTML'i
    // tutabildigi icin, orada etiketlerden arindirilmis kisa bir metin gosterilir;
    // tam bicimli hali sadece kart detay penceresinde (duzenleyicide) goruntulenir.
    public string? DescriptionPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Description))
            {
                return null;
            }

            var text = System.Text.RegularExpressions.Regex.Replace(Description, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length == 0)
            {
                return null;
            }

            return text.Length > 160 ? text[..160] + "…" : text;
        }
    }
}

/// <summary>
/// Mühendis/Admin tarafında "Panolar" listesindeki "..." menüsünün "Yetkileri
/// Görüntüle" seçeneği için: panoyu oluşturan müşteri ve bu müşterinin panoya
/// eklediği diğer yetkili müşteriler (isim + e-posta).
/// </summary>
public class BoardAuthorizationsViewModel
{
    public string OwnerDisplayName { get; set; } = string.Empty;
    public string? OwnerEmail { get; set; }
    public List<BoardAuthorizedPersonViewModel> AuthorizedPeople { get; set; } = new();
}

public class BoardAuthorizedPersonViewModel
{
    /// <summary>Yetkili e-posta sisteme kayıtlı bir hesaba karşılık geliyorsa dolu olur.</summary>
    public string? DisplayName { get; set; }
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// "Panolarım" sayfasında (müşteri rolü) pano listesinin sağındaki özet
/// widget'ı için: toplam pano/açık kart/onay bekleyen kart sayıları ve
/// en son eklenen/taşınan birkaç kartın kısa akışı.
/// </summary>
public class CustomerBoardSummary
{
    public int TotalBoards { get; set; }
    public int OpenCardCount { get; set; }
    public int PendingApprovalCount { get; set; }
    public List<BoardActivityItem> RecentActivity { get; set; } = new();
}

public class BoardActivityItem
{
    public int BoardId { get; set; }
    public string BoardTitle { get; set; } = string.Empty;
    public string CardTitle { get; set; } = string.Empty;
    public string ListLabel { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// "Pano Görevlerim" sayfasında listelenen, kullanıcıya atanmış her kart için:
/// hangi panonun hangi listesinde olduğu ve (mühendis rolüyle) taşınabileceği
/// hedef listeler. Klasik şablonda tek olası hareket (Teste Taşı) sabit
/// kodlanmış olduğundan, aynı görünüm/işlem mantığını jenerik şablonlarla
/// paylaşabilmek için burada da bir BoardListTransition olarak temsil edilir.
/// </summary>
public class MyTaskItemViewModel
{
    public BoardCard Card { get; set; } = new();
    public string TemplateKey { get; set; } = BoardTemplates.Klasik;
    public string ListLabel { get; set; } = string.Empty;
    public List<BoardListTransition> Moves { get; set; } = new();
    public bool IsKlasik => TemplateKey == BoardTemplates.Klasik;
}

/// <summary>
/// Etiketler karta özgüdür (bir etiket her zaman tek bir karta aittir, panodaki
/// başka kartlarla paylaşılmaz); bkz. BoardRepository.GetLabelsForCardAsync.
/// </summary>
public class BoardLabel
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public int CardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// Etiketin kartta gorunup gorunmedigi. Etiketler ekranindaki kutucuk bunu
    /// degistirir; varsayilan renkler secilmemis olarak listelenir, yani karta
    /// kendiliginden eklenmezler.
    /// </summary>
    public bool IsSelected { get; set; }
}

public static class BoardLists
{
    public const string Todo = "todo";
    public const string Test = "test";
    public const string Done = "done";

    public static readonly (string Key, string Label, string IconSvg)[] All =
    {
        (Todo, "Yapılacaklar", "<path d=\"M9 11l3 3L22 4\"/><path d=\"M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11\"/>"),
        (Test, "Test", "<path d=\"M9 2v6l-5 9a2 2 0 0 0 2 3h12a2 2 0 0 0 2-3l-5-9V2\"/><path d=\"M9 2h6\"/><path d=\"M8.5 13h7\"/>"),
        (Done, "Tamamlanan", "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"m9 12 2 2 4-4\"/>")
    };

    // Kolon arkaplan rengi icin izin verilen palet; sunucu tarafinda bu
    // listeye karsi dogrulanir (rastgele CSS degeri inline style'a girmesin).
    // Tonlar, marka lacivertiyle (#235585, navbar) ve acik mavi-gri panel
    // (#EBF0F6) zeminiyle ayni aileden olacak sekilde secildi; eski pastel
    // set panel rengiyle neredeyse ayniydi (Mavi/Gri) ya da temayla alakasiz
    // duruyordu (Sari/Pembe).
    public static readonly (string? Value, string Label)[] ColorPalette =
    {
        (null, "Varsayılan"),
        ("#e3ecdf", "Yeşil"),
        ("#f1ead9", "Sarı"),
        ("#f0e0dd", "Kırmızı"),
        ("#dde8f2", "Mavi"),
        ("#e6e2ef", "Mor"),
        ("#f0e1e8", "Pembe"),
        ("#e9edf2", "Gri")
    };

    public static bool IsValidColor(string? color) =>
        ColorPalette.Any(c => string.Equals(c.Value, color, StringComparison.OrdinalIgnoreCase));
}

// Etiketler kolon arkaplanlarindan daha koyu/canli bir palet kullanir (Trello
// etiket renkleri gibi). Karta otomatik/varsayilan etiket EKLENMEZ; etiketler
// yalnizca kullanici acikca olusturdugunda ortaya cikar (bkz.
// BoardRepository.GetLabelsForCardAsync).
// Renkler marka lacivertiyle (#235585, navbar) ve acik mavi-gri panel
// (#EBF0F6) zeminiyle uyumlu durmasi icin tonu dusurulmus/tonlanmis bir
// palet olarak secildi; eski parlak Tailwind renkleri bu zeminle catisiyordu.
public static class BoardLabelColors
{
    public const string Yellow = "#d9ae55";
    public const string Purple = "#8f83c4";
    public const string Blue = "#5b8bb0";

    /// <summary>
    /// "Etiketleri Düzenle" ekraninin her kart icin hazir listeledigi uc varsayilan
    /// renk. Bunlar SECILMEMIS olarak durur; karta ancak kutucugu isaretlenince eklenir.
    /// </summary>
    public static readonly string[] DefaultPaletteColors = { Yellow, Purple, Blue };

    public static readonly (string Value, string Label)[] Palette =
    {
        (Yellow, "Hardal"),
        ("#c98a5b", "Turuncu"),
        ("#c07568", "Kırmızı"),
        ("#c584a0", "Pembe"),
        (Purple, "Mor"),
        (Blue, "Mavi"),
        ("#4f9aab", "Gök Mavisi"),
        ("#7ea87f", "Yeşil"),
        ("#a3ad6b", "Limon"),
        ("#8a97ab", "Gri")
    };

    public static bool IsValidColor(string? color) =>
        Palette.Any(c => string.Equals(c.Value, color, StringComparison.OrdinalIgnoreCase));
}

// Kart kapagi (Trello cover benzeri) icin duz renk paleti. Etiket renklerinden
// ayri tutuluyor cunku kapak; kartin ust kenarinda genis bir alan/serit olarak
// gosteriliyor ve daha yumusak/pastel tonlar tercih ediliyor. Tonlar, marka
// lacivertiyle (#235585, navbar) ve acik mavi-gri panel (#EBF0F6) zeminiyle
// uyum icin pastelize edildi; eski parlak Tailwind renkleri bu zeminle catisiyordu.
public static class BoardCoverColors
{
    public static readonly (string Value, string Label)[] Palette =
    {
        ("#b9d1b8", "Yeşil"),
        ("#e8d3a0", "Sarı"),
        ("#e0b89a", "Turuncu"),
        ("#dba9a3", "Kırmızı"),
        ("#c9c0e0", "Mor"),
        ("#aac4dc", "Mavi"),
        ("#a8cdd1", "Gök Mavisi"),
        ("#ddbccb", "Pembe"),
        ("#b7c0cd", "Gri")
    };

    public static bool IsValidColor(string? color) =>
        Palette.Any(c => string.Equals(c.Value, color, StringComparison.OrdinalIgnoreCase));
}

// Kullanicinin sagladigi, kapak resmi olarak secilebilen hazir fotograflar.
// wwwroot/assets/board-covers altinda saklaniyor.
public static class BoardCoverPresets
{
    public static readonly (string Key, string Path)[] All = Enumerable.Range(1, 9)
        .Select(i => ($"preset-{i}", $"/assets/board-covers/preset-{i}.jpg"))
        .ToArray();

    public static bool TryGetPath(string? key, out string path)
    {
        var match = All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        path = match.Path ?? string.Empty;
        return !string.IsNullOrEmpty(path);
    }
}

public class CreateBoardViewModel
{
    [Required]
    [Display(Name = "Pano Adı")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Yetkilendirilecek müşteri e-postaları (her satıra bir tane)")]
    public string? AuthorizedEmailsRaw { get; set; }

    public string TemplateKey { get; set; } = BoardTemplates.Klasik;
}

public class AddCardViewModel
{
    [Required]
    [Display(Name = "Başlık")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Açıklama")]
    public string? Description { get; set; }
}

public class RejectCardViewModel
{
    [Required]
    [Display(Name = "Sorunun açıklaması")]
    public string Note { get; set; } = string.Empty;
}

public class CardLabelsPanelModel
{
    public int BoardId { get; set; }
    public int CardId { get; set; }
    public List<BoardLabel> Labels { get; set; } = new();
}

public class LabelEditorPanelModel
{
    public int BoardId { get; set; }
    public int CardId { get; set; }
    public int? LabelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = BoardLabelColors.Yellow;
    public bool IsNew => LabelId is null;
}

public class CardCoverPanelModel
{
    public int BoardId { get; set; }
    public int CardId { get; set; }
    public string? CoverColor { get; set; }
    public string? CoverImagePath { get; set; }
}

public class BoardCardAttachment
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public string AttachmentType { get; set; } = "link";
    public string Url { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsFile => string.Equals(AttachmentType, "file", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => !string.IsNullOrWhiteSpace(FileName) ? FileName : Url;
}

public class BoardCardComment
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public List<BoardCardCommentReaction> Reactions { get; set; } = new();
}

public class BoardCardCommentReaction
{
    public int Id { get; set; }
    public int CommentId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Kart detay penceresi (Kartı Aç) icin butun veriyi bir arada tasir.
/// </summary>
public class CardDetailViewModel
{
    public int BoardId { get; set; }
    public BoardCard Card { get; set; } = new();
    public List<BoardCardAttachment> Attachments { get; set; } = new();
    public List<BoardCardComment> Comments { get; set; } = new();
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserDisplayName { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = string.Empty;
    public bool CanEditCover { get; set; }
}

public class CardAttachmentPanelModel
{
    public int BoardId { get; set; }
    public int CardId { get; set; }
}
