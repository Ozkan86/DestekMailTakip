using task_list.Models;

namespace task_list.Services;

public interface IMailSenderService
{
    /// <summary>
    /// Musteriye yanit gonderir. <paramref name="senderDisplayName"/> yaniti yazan
    /// muhendisin adidir; gonderen adinda ve govdedeki imzada kullanilir, boylece
    /// musteri kendi posta kutusunda yanitlari kimin yazdigini ayirt edebilir.
    /// E-posta adresi degismez (tek SMTP hesabi), yalnizca gorunen ad kisisellesir.
    /// </summary>
    Task SendReplyAsync(
        MailMessageModel originalMail,
        string replyBody,
        IReadOnlyList<ReplyAttachmentPayload> attachments,
        string? senderDisplayName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pano davet/olay bildirimleri gibi basit, duz metinli e-postalar icin.
    /// </summary>
    Task SendNotificationEmailAsync(string toAddress, string subject, string body, CancellationToken cancellationToken);

    /// <summary>
    /// Ayni bildirimi birden fazla alicinin her birine, TEK bir SMTP baglantisi
    /// uzerinden gonderir (alici basina yeniden connect + STARTTLS + AUTH maliyeti
    /// odenmez). Alicilar birbirini gormez; her biri icin ayri mesaj uretilir.
    /// Tek bir alicida hata olursa digerleri denenmeye devam eder.
    /// </summary>
    Task SendNotificationEmailsAsync(IReadOnlyList<string> toAddresses, string subject, string body, CancellationToken cancellationToken);
}
