namespace task_list.Models;

public class MailIndexViewModel
{
    public List<MailMessageModel> Mails { get; set; } = new();
    public int? SelectedId { get; set; }
    public string Folder { get; set; } = MailFolders.Unassigned;
    public List<MailDraftTemplate> DraftTemplates { get; set; } = new();
    public bool OnlyMine { get; set; }

    // Ozel (kullaniciya ozel) klasordeyken baslikta gosterilecek isim; null ise
    // MailFolders.All'daki sabit klasor etiketi kullanilir.
    public string? FolderLabel { get; set; }

    /// <summary>
    /// Klasor gorunumu baska bir sayfaya iframe ile gomulu calisiyor mu
    /// ("İstatistiklerim" sayfasindaki "Bana Ait Görevler" / "Bana Ait
    /// Kapatılmış" bolumleri). Tek fark yerlesimdir: ust cubuk ve klasor
    /// menusu yerine _FrameLayout kullanilir; liste/detay islevleri aynidir.
    /// </summary>
    public bool Embedded { get; set; }
}

public static class MailFolders
{
    public const string Unassigned = "unassigned";
    public const string Mine = "mine";
    public const string Drafts = "drafts";
    public const string Assigned = "assigned";
    public const string Closed = "closed";
    public const string Sent = "sent";
    public const string Archive = "archive";
    public const string Spam = "spam";

    // Kullaniciya ozel klasorler bu prefiksle kodlanir (orn. "custom-42"); sabit
    // klasor listesinin (All) parcasi degildir, ayri bir tablodan (MailUserFolders)
    // dinamik olarak gelir. Boylece folder parametresini isleyen tum action'lar
    // (RedirectAfterAction, DetailFrame, DeleteMail vb.) degismeden kalir.
    public const string CustomFolderPrefix = "custom-";

    // IconSvg: <svg> dışındaki iç içerik (path/circle). feather-icons tarzı,
    // stroke="currentColor" kullanacak şekilde tasarlandı; renk tamamen CSS'ten gelir.
    public static readonly (string Key, string Label, string IconSvg)[] All =
    {
        (Unassigned, "Atanmamış",
            "<path d=\"M22 12h-6l-2 3h-4l-2-3H2\"/><path d=\"M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z\"/>"),
        (Mine, "Bana Ait",
            "<path d=\"M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2\"/><circle cx=\"12\" cy=\"7\" r=\"4\"/>"),
        (Drafts, "Taslaklar",
            "<path d=\"M12 20h9\"/><path d=\"M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4Z\"/>"),
        (Assigned, "Atanmış",
            "<path d=\"M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2\"/><circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M23 21v-2a4 4 0 0 0-3-3.87\"/><path d=\"M16 3.13a4 4 0 0 1 0 7.75\"/>"),
        (Closed, "Kapatılmış",
            "<path d=\"M22 11.08V12a10 10 0 1 1-5.93-9.14\"/><path d=\"M22 4 12 14.01l-3-3\"/>"),
        (Sent, "Gönderilenler",
            "<path d=\"m22 2-7 20-4-9-9-4Z\"/><path d=\"M22 2 11 13\"/>"),
        (Archive, "Arşiv",
            "<path d=\"M21 8v13H3V8\"/><path d=\"M1 3h22v5H1z\"/><path d=\"M10 12h4\"/>"),
        (Spam, "Spam",
            "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"m4.93 4.93 14.14 14.14\"/>")
    };

    public static bool IsValid(string? folder) => All.Any(f => f.Key == folder);

    public static bool IsCustom(string? folder) => folder is not null && folder.StartsWith(CustomFolderPrefix, StringComparison.Ordinal);

    public static bool IsValidOrCustom(string? folder) => IsValid(folder) || IsCustom(folder);

    public static int? ParseCustomFolderId(string? folder)
    {
        if (!IsCustom(folder))
        {
            return null;
        }

        return int.TryParse(folder![CustomFolderPrefix.Length..], out var id) ? id : null;
    }
}
