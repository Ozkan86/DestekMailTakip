namespace task_list.Models;

public class MailAssignmentInfo
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset AssignedAt { get; set; }
}
