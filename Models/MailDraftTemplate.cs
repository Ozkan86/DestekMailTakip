namespace task_list.Models;

public class MailDraftTemplate
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? CreatedByDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? UpdatedByDisplayName { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsPrivate { get; set; }
}
