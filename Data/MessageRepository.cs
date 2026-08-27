using Microsoft.Data.SqlClient;
using task_list.Models;

namespace task_list.Data;

public class MessageRepository : IMessageRepository
{
    private readonly string _connectionString;

    public MessageRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string is not configured.");
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public async Task<int> SendMessageAsync(string? senderUserId, string senderDisplayName, string body, string? recipientUserId, string? recipientDisplayName)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.Messages (SenderUserId, SenderDisplayName, Body, CreatedAt, RecipientUserId, RecipientDisplayName)
            OUTPUT INSERTED.Id
            VALUES (@SenderUserId, @SenderDisplayName, @Body, @Now, @RecipientUserId, @RecipientDisplayName)", connection);
        command.Parameters.AddWithValue("@SenderUserId", (object?)senderUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SenderDisplayName", senderDisplayName);
        command.Parameters.AddWithValue("@Body", body);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@RecipientUserId", (object?)recipientUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@RecipientDisplayName", (object?)recipientDisplayName ?? DBNull.Value);

        var id = await command.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task SendMessagesAsync(IReadOnlyList<MessageDispatch> messages)
    {
        if (messages.Count == 0)
        {
            return;
        }

        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var message in messages)
            {
                using var command = new SqlCommand(@"
                    INSERT INTO dbo.Messages (SenderUserId, SenderDisplayName, Body, CreatedAt, RecipientUserId, RecipientDisplayName)
                    VALUES (@SenderUserId, @SenderDisplayName, @Body, @Now, @RecipientUserId, @RecipientDisplayName)", connection, transaction);
                command.Parameters.AddWithValue("@SenderUserId", (object?)message.SenderUserId ?? DBNull.Value);
                command.Parameters.AddWithValue("@SenderDisplayName", message.SenderDisplayName);
                command.Parameters.AddWithValue("@Body", message.Body);
                command.Parameters.AddWithValue("@Now", now);
                command.Parameters.AddWithValue("@RecipientUserId", (object?)message.RecipientUserId ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecipientDisplayName", (object?)message.RecipientDisplayName ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<EmployeeMessage?> GetByIdAsync(int id)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT Id, SenderUserId, SenderDisplayName, Body, CreatedAt, RecipientUserId, RecipientDisplayName
            FROM dbo.Messages
            WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new EmployeeMessage
        {
            Id = reader.GetInt32(0),
            SenderUserId = reader.IsDBNull(1) ? null : reader.GetString(1),
            SenderDisplayName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            Body = reader.GetString(3),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
            RecipientUserId = reader.IsDBNull(5) ? null : reader.GetString(5),
            RecipientDisplayName = reader.IsDBNull(6) ? null : reader.GetString(6)
        };
    }

    public async Task<List<EmployeeMessage>> GetVisibleMessagesAsync(string userId, int take = 30)
    {
        var result = new List<EmployeeMessage>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT TOP (@Take) msg.Id, msg.SenderUserId, msg.SenderDisplayName, msg.Body, msg.CreatedAt,
                   msg.RecipientUserId, msg.RecipientDisplayName,
                   CASE WHEN mr.UserId IS NULL THEN 0 ELSE 1 END AS IsRead
            FROM dbo.Messages msg
            LEFT JOIN dbo.MessageReads mr ON mr.MessageId = msg.Id AND mr.UserId = @UserId
            WHERE msg.RecipientUserId = @UserId OR msg.RecipientUserId IS NULL
            ORDER BY msg.CreatedAt DESC", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Take", take);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new EmployeeMessage
            {
                Id = reader.GetInt32(0),
                SenderUserId = reader.IsDBNull(1) ? null : reader.GetString(1),
                SenderDisplayName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Body = reader.GetString(3),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
                RecipientUserId = reader.IsDBNull(5) ? null : reader.GetString(5),
                RecipientDisplayName = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsRead = reader.GetInt32(7) == 1
            });
        }

        return result;
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT COUNT(*)
            FROM dbo.Messages msg
            LEFT JOIN dbo.MessageReads mr ON mr.MessageId = msg.Id AND mr.UserId = @UserId
            WHERE (msg.RecipientUserId = @UserId OR msg.RecipientUserId IS NULL)
              AND mr.UserId IS NULL", connection);
        command.Parameters.AddWithValue("@UserId", userId);

        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt32(count);
    }

    public async Task MarkReadAsync(int messageId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.MessageReads (MessageId, UserId, ReadAt)
            SELECT @MessageId, @UserId, @Now
            WHERE NOT EXISTS (SELECT 1 FROM dbo.MessageReads WHERE MessageId = @MessageId AND UserId = @UserId)", connection);
        command.Parameters.AddWithValue("@MessageId", messageId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    public async Task MarkAllReadAsync(string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.MessageReads (MessageId, UserId, ReadAt)
            SELECT msg.Id, @UserId, @Now
            FROM dbo.Messages msg
            WHERE (msg.RecipientUserId = @UserId OR msg.RecipientUserId IS NULL)
              AND NOT EXISTS (SELECT 1 FROM dbo.MessageReads mr WHERE mr.MessageId = msg.Id AND mr.UserId = @UserId)", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }
}
