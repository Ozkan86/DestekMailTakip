namespace task_list.Models;

public class MailReplyAttachment
{
    public int Id { get; set; }
    public int MailReplyId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public bool IsImage { get; set; }

    public string WebPath => "/" + FilePath.Replace("\\", "/");
}