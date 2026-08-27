namespace task_list.Models;

public class MailMessageModel
{
    public int Id { get; set; }
    public string ImapUid { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? BodyText { get; set; }
    public string? BodyHtml { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public bool IsRead { get; set; }

    public string? TaskName { get; set; }

    public string FlagType { get; set; } = MailFlagTypes.Active;
    public string? FlagSetByUserId { get; set; }
    public string? FlagSetByDisplayName { get; set; }
    public DateTimeOffset? FlagSetAt { get; set; }

    public string? CategoryColorKey { get; set; }
    public List<MailTagInfo> Tags { get; set; } = new();

    public List<MailAssignmentInfo> AssignedUsers { get; set; } = new();
    public bool IsAssigned => AssignedUsers.Count > 0;

    public string? DraftBody { get; set; }
    public string? DraftUpdatedByDisplayName { get; set; }
    public DateTimeOffset? DraftUpdatedAt { get; set; }

    public List<MailAttachment> Attachments { get; set; } = new();
    public List<MailReply> Replies { get; set; } = new();

    public List<MailAttachment> ImageAttachments => Attachments.Where(a => a.IsImage).ToList();
    public bool HasDraft => !string.IsNullOrEmpty(DraftBody);

    // Su iki alan kullaniciya ozeldir; GetByIdAsync tarafindan doldurulmaz,
    // ihtiyaci olan controller action'lari (Details/DetailFrame) ayrica doldurur.
    public bool IsArchived { get; set; }
    public HashSet<int> FolderIds { get; set; } = new();
}

public class MailUserFolder
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class MailTagInfo
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string ColorKey { get; set; } = TagPalette.DefaultKey;
}

/// <summary>
/// Bir mail'in her zaman tam olarak sahip oldugu tek bayrak. Bayragi kim
/// koyarsa (FlagSetByUserId) sadece o kullanici degistirebilir/kaldirabilir.
/// </summary>
public static class MailFlagTypes
{
    public const string Active = "active";
    public const string Pending = "pending";
    public const string Closed = "closed";
    public const string Spam = "spam";

    public static readonly (string Key, string Label)[] All =
    {
        (Active, "Aktif"),
        (Pending, "Beklemede"),
        (Closed, "Tamamlandı"),
        (Spam, "Spam")
    };

    public static bool IsValid(string? flagType) => All.Any(f => f.Key == flagType);
}

/// <summary>
/// Kategori belirteci (task koseleri) ve tag'lerde kullanilan, marka
/// paletiyle uyumlu sabit 6 renklik set.
/// </summary>
public static class TagPalette
{
    public const string DefaultKey = "gray";

    public static readonly (string Key, string Label, string Color)[] All =
    {
        ("red", "Mobil", "#b8503f"),
        ("green", "Web", "#5b8a72"),
        ("blue", "Backend", "#3d7a9e"),
        ("purple", "Entegrasyon", "#8b6f9e"),
        ("amber", "Acil", "#f5a623"),
        ("gray", "Genel", "#6b7280")
    };

    public static string ColorOf(string? key) => All.FirstOrDefault(c => c.Key == key).Color is { Length: > 0 } color
        ? color
        : All.First(c => c.Key == DefaultKey).Color;
}
