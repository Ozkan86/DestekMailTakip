namespace task_list.Models;

public class EmployeeMessage
{
    public int Id { get; set; }
    public string? SenderUserId { get; set; }
    public string SenderDisplayName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? RecipientUserId { get; set; }
    public string? RecipientDisplayName { get; set; }
    public bool IsBroadcast => RecipientUserId is null;
    public bool IsRead { get; set; }
}
