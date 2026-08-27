namespace task_list.Models;

public class MailReply
{
    public int Id { get; set; }
    public int MailMessageId { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? SentByUserId { get; set; }
    public string SentByDisplayName { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }

    public bool IsInbound { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ImapUid { get; set; }

    public List<MailReplyAttachment> Attachments { get; set; } = new();
}
