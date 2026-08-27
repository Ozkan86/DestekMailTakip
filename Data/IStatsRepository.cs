using task_list.Models;

namespace task_list.Data;

/// <summary>
/// "İstatistiklerim" sayfası (mühendis profil menüsündeki rozet) için mail ve
/// pano istatistiklerini, decay'li olay günlüğünü ve kullanıcıya özel notları yönetir.
/// </summary>
public interface IStatsRepository
{
    Task<EngineerMailStats> GetMailStatsAsync(string userId);
    Task<EngineerBoardStats> GetBoardStatsAsync(string userId);

    Task<List<EngineerMessageDrawerItem>> GetNewMessageTasksAsync(string userId);

    /// <summary>
    /// Verilen StatKey'lerden herhangi birine ait, süresi dolmamış (decay
    /// içinde) olay satırlarını pano bağlamıyla döner.
    /// </summary>
    Task<List<EngineerCardEventDrawerItem>> GetCardEventsAsync(string userId, IEnumerable<string> statKeys);

    /// <summary>
    /// Kullanıcının şu an SeenAt IS NULL olan tüm olay satırlarını "şimdi görüldü"
    /// olarak işaretler (İstatistiklerim sayfası her açıldığında çağrılır).
    /// </summary>
    Task MarkAllStatEventsSeenAsync(string userId);

    /// <summary>Bir decay'li istatistik olayı kaydeder (aktör == sahip ise çağıran taraf atlamalıdır).</summary>
    Task LogStatEventAsync(string userId, string statKey, string entityType, int entityId, int? boardId, DateTimeOffset eventAt);

    /// <summary>Verilen kartın sahibi dışındaki tüm Employee rolündeki kullanıcı id'lerini döner (fan-out için).</summary>
    Task<List<string>> GetOtherEmployeeUserIdsAsync(string excludeUserId1, string? excludeUserId2);

    Task<List<EngineerNote>> GetNotesAsync(string userId);
    Task<EngineerNote> AddNoteAsync(string userId, string body);
    Task DeleteNoteAsync(int noteId, string userId);

    /// <summary>Var olan bir notun metnini gunceller (sadece sahibi). Bulunamazsa/sahibi degilse null doner.</summary>
    Task<EngineerNote?> UpdateNoteAsync(int noteId, string userId, string body);

    /// <summary>
    /// Kullanicinin notlarini, surukle-birak ile belirlenen yeni sirayla (ilk eleman
    /// en ustte) SortOrder'a yazar. Listede olmayan/baska kullaniciya ait id'ler yok sayilir.
    /// </summary>
    Task ReorderNotesAsync(string userId, IReadOnlyList<int> orderedNoteIds);
}
