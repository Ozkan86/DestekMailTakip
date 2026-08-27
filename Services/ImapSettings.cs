namespace task_list.Services;

public class ImapSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 60;
    public string MailboxName { get; set; } = "INBOX";
}
