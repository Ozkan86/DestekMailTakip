using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using task_list.Models;

namespace task_list.Services;

public class MailSenderService : IMailSenderService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<MailSenderService> _logger;

    public MailSenderService(IOptions<SmtpSettings> settings, ILogger<MailSenderService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendReplyAsync(
        MailMessageModel originalMail,
        string replyBody,
        IReadOnlyList<ReplyAttachmentPayload> attachments,
        string? senderDisplayName,
        CancellationToken cancellationToken)
    {
        var message = BuildReplyMessage(originalMail, replyBody, attachments, senderDisplayName);

        using var client = new SmtpClient();

        await client.ConnectAsync(_settings.Host, _settings.Port,
            _settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
            cancellationToken);
        await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>
    /// Yanit e-postasini kurar (SMTP baglantisi olmadan); gonderim disinda
    /// dogrulanabilmesi icin ayri bir metot olarak duruyor.
    /// </summary>
    public MimeMessage BuildReplyMessage(
        MailMessageModel originalMail,
        string replyBody,
        IReadOnlyList<ReplyAttachmentPayload> attachments,
        string? senderDisplayName)
    {
        var message = new MimeMessage();

        // Gonderen adi yaniti yazan muhendisi tasir ("Burak Yıldırım (Creamobile
        // Destek)"), adres ise tek SMTP hesabi olarak kalir; boylece SPF/DKIM
        // etkilenmez ama musteri gelen kutusunda kimin yazdigini gorur.
        // Yanitlar yine destek kutusuna dussun diye Reply-To kurumsal kimlikte.
        message.From.Add(new MailboxAddress(BuildFromDisplayName(senderDisplayName), _settings.Username));
        message.ReplyTo.Add(new MailboxAddress(_settings.FromDisplayName, _settings.Username));
        message.To.Add(MailboxAddress.Parse(originalMail.FromAddress));

        message.Subject = originalMail.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? originalMail.Subject
            : $"Re: {originalMail.Subject}";

        if (!string.IsNullOrEmpty(originalMail.MessageId))
        {
            message.InReplyTo = originalMail.MessageId;
            message.References.Add(originalMail.MessageId);
        }

        // Imza yalnizca gonderilen kopyaya eklenir; uygulamada saklanan yanit
        // govdesi (MailReplies.Body) imzasiz kalir, aksi halde konusma akisinda
        // her yanitin altinda tekrar ederdi.
        var builder = new BodyBuilder { TextBody = AppendSignature(replyBody, senderDisplayName) };

        foreach (var attachment in attachments)
        {
            var contentType = ContentType.Parse(
                string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType);
            builder.Attachments.Add(attachment.FileName, attachment.Content, contentType);
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    /// <summary>"Burak Yıldırım (Creamobile Destek)" — ad bilinmiyorsa yalnizca kurum adi.</summary>
    private string BuildFromDisplayName(string? senderDisplayName)
    {
        if (string.IsNullOrWhiteSpace(senderDisplayName))
        {
            return _settings.FromDisplayName;
        }

        return string.IsNullOrWhiteSpace(_settings.FromDisplayName)
            ? senderDisplayName.Trim()
            : $"{senderDisplayName.Trim()} ({_settings.FromDisplayName})";
    }

    /// <summary>
    /// Govdenin sonuna standart imza ayraciyla ("-- ") imza ekler; bu ayraci
    /// taniyan istemciler imzayi alintilarda katlayarak gosterir.
    /// </summary>
    private string AppendSignature(string body, string? senderDisplayName)
    {
        if (string.IsNullOrWhiteSpace(senderDisplayName))
        {
            return body;
        }

        var signature = string.IsNullOrWhiteSpace(_settings.FromDisplayName)
            ? senderDisplayName.Trim()
            : $"{senderDisplayName.Trim()}{Environment.NewLine}{_settings.FromDisplayName}";

        return $"{body}{Environment.NewLine}{Environment.NewLine}-- {Environment.NewLine}{signature}";
    }

    public async Task SendNotificationEmailAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        await SendNotificationEmailsAsync(new[] { toAddress }, subject, body, cancellationToken);
    }

    public async Task SendNotificationEmailsAsync(IReadOnlyList<string> toAddresses, string subject, string body, CancellationToken cancellationToken)
    {
        if (toAddresses.Count == 0)
        {
            return;
        }

        using var client = new SmtpClient();

        await client.ConnectAsync(_settings.Host, _settings.Port,
            _settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
            cancellationToken);
        await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);

        try
        {
            foreach (var toAddress in toAddresses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress(_settings.FromDisplayName, _settings.Username));
                    message.To.Add(MailboxAddress.Parse(toAddress));
                    message.Subject = subject;
                    message.Body = new TextPart("plain") { Text = body };

                    await client.SendAsync(message, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Tek bir alicidaki hata (gecersiz adres vb.) digerlerini engellemesin.
                    _logger.LogWarning(ex, "Bildirim e-postası gönderilemedi: {Address}", toAddress);
                }
            }
        }
        finally
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
    }
}
