namespace task_list.Models;

/// <summary>
/// Decay'li (görüldükten 4 saat sonra sayacı etkilemeyen) istatistik olaylarının
/// StatKey degerleri. bkz. dbo.EngineerStatEvents / IStatsRepository.
/// </summary>
public static class EngineerStatKeys
{
    public const string MailClosed = "MailClosed";
    public const string MailAssignedToMe = "MailAssignedToMe";
    public const string CardApprovedMine = "CardApprovedMine";
    public const string CardRejectedMine = "CardRejectedMine";
    public const string CardApprovedOther = "CardApprovedOther";
    public const string CardRejectedOther = "CardRejectedOther";
    public const string CardLabelAdded = "CardLabelAdded";
    public const string CardCommentAdded = "CardCommentAdded";

    /// <summary>Bir gorunumun (4 saat) gecerlilik suresi.</summary>
    public static readonly TimeSpan SeenExpiry = TimeSpan.FromHours(4);
}

/// <summary>"İstatistiklerim" sayfasının üst-sol (mail/destek) bloğu.</summary>
public class EngineerMailStats
{
    public int TotalMine { get; set; }
    public int ClosedCount { get; set; }
    public int NewMessagesCount { get; set; }
    public int NewAssignedCount { get; set; }
}

/// <summary>"İstatistiklerim" sayfasının üst-sağ (pano) bloğu.</summary>
public class EngineerBoardStats
{
    public int MyCardsCount { get; set; }
    public int OtherApprovedCount { get; set; }
    public int OtherRejectedCount { get; set; }
    public int MyApprovedCount { get; set; }
    public int MyRejectedCount { get; set; }
    public int LabelsAddedCount { get; set; }
    public int CommentsAddedCount { get; set; }
}

public class EngineerStatsPageViewModel
{
    public EngineerMailStats Mail { get; set; } = new();
    public EngineerBoardStats Board { get; set; } = new();
    public List<EngineerNote> Notes { get; set; } = new();
}

/// <summary>"Görevlerime ait mesajlar" çekmecesindeki bir satır.</summary>
public class EngineerMessageDrawerItem
{
    public int MailId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string Folder { get; set; } = MailFolders.Unassigned;
    public string FolderLabel { get; set; } = string.Empty;
    public DateTimeOffset LastMessageAt { get; set; }
}

/// <summary>
/// "Diğer onay/red", "Bana ait onay/red" ve "Etiketler/yorumlar" çekmecelerinde
/// ortak kullanılan pano-bağlamlı olay satırı.
/// </summary>
public class EngineerCardEventDrawerItem
{
    public int BoardId { get; set; }
    public string BoardTitle { get; set; } = string.Empty;
    public int CardId { get; set; }
    public string CardTitle { get; set; } = string.Empty;
    public string StatKey { get; set; } = string.Empty;
    public DateTimeOffset EventAt { get; set; }

    public string EventLabel => StatKey switch
    {
        EngineerStatKeys.CardApprovedMine or EngineerStatKeys.CardApprovedOther => "Onaylandı",
        EngineerStatKeys.CardRejectedMine or EngineerStatKeys.CardRejectedOther => "Reddedildi",
        EngineerStatKeys.CardLabelAdded => "Yeni etiket",
        EngineerStatKeys.CardCommentAdded => "Yeni yorum",
        _ => StatKey
    };
}

public class EngineerNote
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int SortOrder { get; set; }
}
