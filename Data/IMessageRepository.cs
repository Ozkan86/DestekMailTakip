using task_list.Models;

namespace task_list.Data;

/// <summary>Toplu uygulama-ici mesaj yaziminda tek bir alici satiri.</summary>
public record MessageDispatch(
    string? SenderUserId,
    string SenderDisplayName,
    string Body,
    string? RecipientUserId,
    string? RecipientDisplayName);

public interface IMessageRepository
{
    Task<int> SendMessageAsync(string? senderUserId, string senderDisplayName, string body, string? recipientUserId, string? recipientDisplayName);

    /// <summary>
    /// Birden fazla mesaji tek baglanti + tek transaction icinde yazar (alici basina
    /// ayri baglanti acmaz). Bildirim fan-out'u icin kullanilir.
    /// </summary>
    Task SendMessagesAsync(IReadOnlyList<MessageDispatch> messages);

    Task<EmployeeMessage?> GetByIdAsync(int id);

    /// <summary>
    /// Kullanicinin gorebilecegi mesajlar: kendisine hedeflenenler + tum genel yayinlar.
    /// </summary>
    Task<List<EmployeeMessage>> GetVisibleMessagesAsync(string userId, int take = 30);

    Task<int> GetUnreadCountAsync(string userId);
    Task MarkReadAsync(int messageId, string userId);
    Task MarkAllReadAsync(string userId);
}
