using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using task_list.Data;
using task_list.Models;

namespace task_list.Services;

public class ImapMailService : IImapMailService
{
    private readonly ImapSettings _settings;
    private readonly IMailRepository _mailRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ImapMailService> _logger;

    public ImapMailService(
        IOptions<ImapSettings> settings,
        IMailRepository mailRepository,
        IMessageRepository messageRepository,
        IWebHostEnvironment environment,
        ILogger<ImapMailService> logger)
    {
        _settings = settings.Value;
        _mailRepository = mailRepository;
        _messageRepository = messageRepository;
        _environment = environment;
        _logger = logger;
    }

    public async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        var existingUids = await _mailRepository.GetExistingImapUidsAsync();
        var messageIdToMailId = await _mailRepository.GetMessageIdToMailIdMapAsync();

        using var client = new ImapClient();

        await client.ConnectAsync(_settings.Host, _settings.Port,
            _settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
            cancellationToken);
        await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);

        var imported = await SyncFolderAsync(client.Inbox, uidPrefix: null, existingUids, messageIdToMailId, cancellationToken);

        // Onemli mailler Gmail tarafinda Spam klasorune dusebiliyor; oradakileri de
        // cekip Unassigned'a dahil ediyoruz (spam karari sistemde hala elle veriliyor).
        try
        {
            var junkFolder = client.GetFolder(SpecialFolder.Junk)
                ?? throw new FolderNotFoundException("Spam/Junk klasoru bulunamadi.");
            imported += await SyncFolderAsync(junkFolder, uidPrefix: "spam", existingUids, messageIdToMailId, cancellationToken);
        }
        catch (Exception ex) when (ex is NotSupportedException or FolderNotFoundException)
        {
            _logger.LogWarning(ex, "Spam/Junk klasoru bulunamadi veya desteklenmiyor, atlaniyor.");
        }

        await client.DisconnectAsync(true, cancellationToken);
        return imported;
    }

    /// <summary>
    /// Bu klasor icin aranmasi gereken ilk UID: zaten kayitli olan en buyuk UID'nin
    /// bir fazlasi. IMAP'te UID'ler klasor icinde artan sirada verildigi icin daha
    /// kucuk UID'lerin yeniden taranmasina gerek yoktur; boylece arama maliyeti
    /// posta kutusu buyudukce artmaz (eskiden SearchQuery.All ile TUM UID'ler
    /// her senkronizasyonda cekiliyordu).
    /// </summary>
    private static UniqueId GetSearchStartUid(HashSet<string> existingUids, string? uidPrefix)
    {
        var prefix = uidPrefix is null ? null : uidPrefix + ":";
        uint max = 0;

        foreach (var uidText in existingUids)
        {
            string numberPart;
            if (prefix is null)
            {
                // On eksiz kayitlar INBOX'a aittir.
                if (uidText.Contains(':'))
                {
                    continue;
                }
                numberPart = uidText;
            }
            else
            {
                if (!uidText.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                numberPart = uidText[prefix.Length..];
            }

            if (uint.TryParse(numberPart, out var value) && value > max)
            {
                max = value;
            }
        }

        return new UniqueId(max + 1);
    }

    private async Task<int> SyncFolderAsync(
        IMailFolder folder,
        string? uidPrefix,
        HashSet<string> existingUids,
        Dictionary<string, int> messageIdToMailId,
        CancellationToken cancellationToken)
    {
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        // Sadece son kayitli UID'den sonrasini ara (artimli senkronizasyon).
        var startUid = GetSearchStartUid(existingUids, uidPrefix);

        // Guvenlik agi: UIDVALIDITY degistiyse (posta kutusu sunucu tarafinda yeniden
        // olusturulmus) UID'ler bastan baslar ve isaretimiz sunucununkinden buyuk kalir;
        // bu durumda artimli arama yeni mailleri sonsuza dek atlardi. Sunucunun bir
        // sonraki UID'i bizim isaretimizin altindaysa tam taramaya geri donuyoruz.
        if (folder.UidNext.HasValue && folder.UidNext.Value.Id < startUid.Id)
        {
            _logger.LogWarning(
                "IMAP UID isareti sunucunun gerisinde kaldi (UidNext={UidNext} < baslangic={Start}); tam tarama yapiliyor.",
                folder.UidNext.Value.Id, startUid.Id);
            startUid = new UniqueId(1);
        }

        var uids = await folder.SearchAsync(
            MailKit.Search.SearchQuery.Uids(new UniqueIdRange(startUid, UniqueId.MaxValue)),
            cancellationToken);

        var imported = 0;

        foreach (var uid in uids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // IMAP UID'leri sadece klasor icinde benzersizdir; farkli klasorler arasinda
            // (ozellikle Spam) carpismayi onlemek icin klasore ozgu bir on ek ekliyoruz.
            var uidText = uidPrefix is null ? uid.ToString() : $"{uidPrefix}:{uid}";
            if (existingUids.Contains(uidText))
            {
                continue;
            }

            var message = await folder.GetMessageAsync(uid, cancellationToken);
            var parentMailId = FindThreadParent(message, messageIdToMailId);

            if (parentMailId is int mailId)
            {
                await SaveInboundReplyAsync(mailId, uidText, message, cancellationToken);
            }
            else
            {
                var newMailId = await SaveMessageAsync(uidText, message);
                await SaveAttachmentsAsync(newMailId, message, cancellationToken);

                if (!string.IsNullOrEmpty(message.MessageId))
                {
                    messageIdToMailId[message.MessageId] = newMailId;
                }
            }

            existingUids.Add(uidText);
            imported++;
        }

        return imported;
    }

    private static int? FindThreadParent(MimeMessage message, Dictionary<string, int> messageIdToMailId)
    {
        if (!string.IsNullOrEmpty(message.InReplyTo) && messageIdToMailId.TryGetValue(message.InReplyTo, out var mailId))
        {
            return mailId;
        }

        foreach (var reference in message.References)
        {
            if (messageIdToMailId.TryGetValue(reference, out var referencedMailId))
            {
                return referencedMailId;
            }
        }

        return null;
    }

    private async Task SaveInboundReplyAsync(int mailId, string uidText, MimeMessage message, CancellationToken cancellationToken)
    {
        var from = message.From.Mailboxes.FirstOrDefault();

        var replyId = await _mailRepository.InsertInboundReplyAsync(new MailReply
        {
            MailMessageId = mailId,
            Body = ExtractPlainText(message),
            SentAt = message.Date,
            FromAddress = from?.Address ?? string.Empty,
            FromName = from?.Name ?? string.Empty,
            ImapUid = uidText
        });

        await SaveReplyAttachmentsAsync(replyId, message, cancellationToken);
        await _mailRepository.MarkAsUnreadAsync(mailId);

        var mail = await _mailRepository.GetByIdAsync(mailId);
        if (mail is null)
        {
            return;
        }

        foreach (var assignee in mail.AssignedUsers)
        {
            await _messageRepository.SendMessageAsync(
                null,
                "Sistem",
                NotificationTextHelper.InboundMailArrivedText(mail.Subject),
                assignee.UserId,
                assignee.DisplayName);
        }
    }

    private static string ExtractPlainText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return message.TextBody;
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            var stripped = Regex.Replace(message.HtmlBody, "<[^>]+>", " ");
            return System.Net.WebUtility.HtmlDecode(stripped).Trim();
        }

        return string.Empty;
    }

    private async Task<int> SaveMessageAsync(string uidText, MimeMessage message)
    {
        var from = message.From.Mailboxes.FirstOrDefault();

        var mail = new MailMessageModel
        {
            ImapUid = uidText,
            MessageId = message.MessageId ?? string.Empty,
            FromAddress = from?.Address ?? string.Empty,
            FromName = from?.Name ?? string.Empty,
            Subject = message.Subject ?? string.Empty,
            BodyText = message.TextBody,
            BodyHtml = message.HtmlBody,
            ReceivedAt = message.Date,
            IsRead = false
        };

        return await _mailRepository.InsertMailAsync(mail);
    }

    // message.Attachments sadece Content-Disposition: attachment olan parcalari doner.
    // Mail govdesine gomulu (inline, <img src="cid:..."> ile referanslanan) resimler bu listede
    // YER ALMAZ; onlari yakalamak icin butun govde parcalarini (BodyParts) taramak gerekir.
    private async Task SaveAttachmentsAsync(int mailId, MimeMessage message, CancellationToken cancellationToken)
    {
        var parts = message.BodyParts
            .OfType<MimePart>()
            .Where(part => part.IsAttachment || part.ContentType?.MediaType == "image")
            .ToList();

        if (parts.Count == 0)
        {
            return;
        }

        var relativeFolder = Path.Combine("uploads", mailId.ToString());
        var absoluteFolder = Path.Combine(_environment.WebRootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var htmlBody = message.HtmlBody;
        var htmlChanged = false;

        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (part.Content is null)
            {
                continue;
            }

            var safeFileName = ResolveFileName(part);
            var uniqueFileName = $"{Guid.NewGuid():N}_{safeFileName}";
            var absolutePath = Path.Combine(absoluteFolder, uniqueFileName);

            await using (var stream = File.Create(absolutePath))
            {
                await part.Content.DecodeToAsync(stream, cancellationToken);
            }

            var attachment = new MailAttachment
            {
                MailMessageId = mailId,
                FileName = safeFileName,
                FilePath = Path.Combine(relativeFolder, uniqueFileName),
                ContentType = part.ContentType?.MimeType ?? string.Empty,
                IsImage = part.ContentType?.MediaType == "image"
            };

            try
            {
                await _mailRepository.InsertAttachmentAsync(mailId, attachment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ek kaydedilirken hata olustu: {FileName}", safeFileName);
                continue;
            }

            // Govde HTML'i icinde "cid:..." ile bu parcaya referans veriliyorsa,
            // gercekte kaydettigimiz dosyanin web yoluyla degistiriyoruz.
            if (!string.IsNullOrEmpty(part.ContentId) && !string.IsNullOrEmpty(htmlBody))
            {
                var cidReference = $"cid:{part.ContentId}";
                if (htmlBody.Contains(cidReference, StringComparison.OrdinalIgnoreCase))
                {
                    htmlBody = htmlBody.Replace(cidReference, attachment.WebPath, StringComparison.OrdinalIgnoreCase);
                    htmlChanged = true;
                }
            }
        }

        if (htmlChanged && htmlBody is not null)
        {
            await _mailRepository.UpdateMailBodyHtmlAsync(mailId, htmlBody);
        }
    }

    // Gelen (musteriden) yanitlarin ekleri; inline cid resimleri burada ayrica
    // govdeye gomulmuyor (yanit govdesi duz metin olarak saklaniyor), sadece
    // indirilebilir ek olarak listeleniyor.
    private async Task SaveReplyAttachmentsAsync(int replyId, MimeMessage message, CancellationToken cancellationToken)
    {
        var parts = message.BodyParts
            .OfType<MimePart>()
            .Where(part => part.IsAttachment || part.ContentType?.MediaType == "image")
            .ToList();

        if (parts.Count == 0)
        {
            return;
        }

        var relativeFolder = Path.Combine("uploads", "replies", replyId.ToString());
        var absoluteFolder = Path.Combine(_environment.WebRootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (part.Content is null)
            {
                continue;
            }

            var safeFileName = ResolveFileName(part);
            var uniqueFileName = $"{Guid.NewGuid():N}_{safeFileName}";
            var absolutePath = Path.Combine(absoluteFolder, uniqueFileName);

            await using (var stream = File.Create(absolutePath))
            {
                await part.Content.DecodeToAsync(stream, cancellationToken);
            }

            try
            {
                await _mailRepository.InsertReplyAttachmentAsync(replyId, new MailReplyAttachment
                {
                    MailReplyId = replyId,
                    FileName = safeFileName,
                    FilePath = Path.Combine(relativeFolder, uniqueFileName),
                    ContentType = part.ContentType?.MimeType ?? string.Empty,
                    IsImage = part.ContentType?.MediaType == "image"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Yanit eki kaydedilirken hata olustu: {FileName}", safeFileName);
            }
        }
    }

    private static string ResolveFileName(MimePart part)
    {
        if (!string.IsNullOrEmpty(part.FileName))
        {
            return Path.GetFileName(part.FileName);
        }

        var extension = part.ContentType?.MediaSubtype ?? "bin";
        var baseName = part.ContentId?.Trim('<', '>') ?? Guid.NewGuid().ToString("N");
        return $"{baseName}.{extension}";
    }
}
