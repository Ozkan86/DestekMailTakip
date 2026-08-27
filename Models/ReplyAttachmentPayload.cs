namespace task_list.Models;

public record ReplyAttachmentPayload(string FileName, string ContentType, byte[] Content);
