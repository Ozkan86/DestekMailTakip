using Microsoft.Data.SqlClient;
using task_list.Models;

namespace task_list.Data;

public class StatsRepository : IStatsRepository
{
    private readonly string _connectionString;

    public StatsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string is not configured.");
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    // "SeenAt gorulduysa, gorulmesinden 240 dakikadan (4 saat) az gecmisse hala sayilir" kosulu.
    private const string ActiveEventClause =
        "(SeenAt IS NULL OR DATEDIFF(MINUTE, SeenAt, SYSDATETIMEOFFSET()) < 240)";

    public async Task<EngineerMailStats> GetMailStatsAsync(string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        var stats = new EngineerMailStats();

        using (var command = new SqlCommand(@"
            SELECT COUNT(*)
            FROM dbo.Mails m
            WHERE EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id AND ma.UserId = @UserId)
              AND m.FlagType NOT IN ('closed', 'spam')", connection))
        {
            command.Parameters.AddWithValue("@UserId", userId);
            stats.TotalMine = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        using (var command = new SqlCommand($@"
            SELECT COUNT(*) FROM dbo.EngineerStatEvents
            WHERE UserId = @UserId AND StatKey = @StatKey AND {ActiveEventClause}", connection))
        {
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@StatKey", EngineerStatKeys.MailClosed);
            stats.ClosedCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        using (var command = new SqlCommand($@"
            SELECT COUNT(*) FROM dbo.EngineerStatEvents
            WHERE UserId = @UserId AND StatKey = @StatKey AND {ActiveEventClause}", connection))
        {
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@StatKey", EngineerStatKeys.MailAssignedToMe);
            stats.NewAssignedCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        // Gorev bir sekilde uzerimizdeyse (kendimiz aldiysak da, baskasi atadiysa da)
        // o gorevdeki musteri ve diger mühendis mesajlari icin bildirim aliriz;
        // ayrica bizim daha once yanit vermis olmamiz GEREKMEZ.
        using (var command = new SqlCommand(@"
            SELECT COUNT(*)
            FROM dbo.Mails m
            LEFT JOIN dbo.MailReplySeen mrs ON mrs.MailId = m.Id AND mrs.UserId = @UserId
            WHERE EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id AND ma.UserId = @UserId)
              AND EXISTS (
                  SELECT 1 FROM dbo.MailReplies r2
                  WHERE r2.MailMessageId = m.Id
                    AND (r2.IsInbound = 1 OR (r2.SentByUserId IS NOT NULL AND r2.SentByUserId <> @UserId))
                    AND r2.SentAt > ISNULL(mrs.LastSeenAt, CAST('1900-01-01' AS DATETIMEOFFSET)))", connection))
        {
            command.Parameters.AddWithValue("@UserId", userId);
            stats.NewMessagesCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        return stats;
    }

    public async Task<EngineerBoardStats> GetBoardStatsAsync(string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        var stats = new EngineerBoardStats();

        // "Bana ait" = "Pano Görevlerim" listesiyle AYNI kart kumesi: uzerine alinmis
        // ve bulundugu listede hala mühendis aksiyonu bekleyen kartlar. Musteriye
        // devredilmis (orn. UAT Review) veya bitmis (Done) kartlar sayilmaz; bu kural
        // sablona bagli oldugu icin SQL'de degil, BoardTemplates uzerinden uygulanir.
        using (var command = new SqlCommand(@"
            SELECT c.ListKey, b.TemplateKey
            FROM dbo.BoardCards c
            INNER JOIN dbo.Boards b ON b.Id = c.BoardId
            WHERE c.AssignedUserId = @UserId AND c.IsArchived = 0 AND b.IsPreview = 0", connection))
        {
            command.Parameters.AddWithValue("@UserId", userId);

            var count = 0;
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var listKey = reader.IsDBNull(0) ? null : reader.GetString(0);
                var templateKey = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (BoardTemplates.HasPendingEngineerAction(templateKey, listKey))
                {
                    count++;
                }
            }

            stats.MyCardsCount = count;
        }

        async Task<int> CountEventsAsync(string statKey)
        {
            using var command = new SqlCommand($@"
                SELECT COUNT(*) FROM dbo.EngineerStatEvents
                WHERE UserId = @UserId AND StatKey = @StatKey AND {ActiveEventClause}", connection);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@StatKey", statKey);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        stats.OtherApprovedCount = await CountEventsAsync(EngineerStatKeys.CardApprovedOther);
        stats.OtherRejectedCount = await CountEventsAsync(EngineerStatKeys.CardRejectedOther);
        stats.MyApprovedCount = await CountEventsAsync(EngineerStatKeys.CardApprovedMine);
        stats.MyRejectedCount = await CountEventsAsync(EngineerStatKeys.CardRejectedMine);
        stats.LabelsAddedCount = await CountEventsAsync(EngineerStatKeys.CardLabelAdded);
        stats.CommentsAddedCount = await CountEventsAsync(EngineerStatKeys.CardCommentAdded);

        return stats;
    }

    public async Task<List<EngineerMessageDrawerItem>> GetNewMessageTasksAsync(string userId)
    {
        var result = new List<EngineerMessageDrawerItem>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT m.Id, m.Subject, m.FromName, m.FlagType,
                CASE WHEN EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id AND ma.UserId = @UserId) THEN 1 ELSE 0 END AS IsMine,
                CASE WHEN EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id) THEN 1 ELSE 0 END AS HasAssignment,
                lm.LastMessageAt
            FROM dbo.Mails m
            LEFT JOIN dbo.MailReplySeen mrs ON mrs.MailId = m.Id AND mrs.UserId = @UserId
            CROSS APPLY (
                SELECT MAX(r2.SentAt) AS LastMessageAt
                FROM dbo.MailReplies r2
                WHERE r2.MailMessageId = m.Id
                  AND (r2.IsInbound = 1 OR (r2.SentByUserId IS NOT NULL AND r2.SentByUserId <> @UserId))
                  AND r2.SentAt > ISNULL(mrs.LastSeenAt, CAST('1900-01-01' AS DATETIMEOFFSET))
            ) lm
            WHERE EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id AND ma.UserId = @UserId)
              AND lm.LastMessageAt IS NOT NULL
            ORDER BY lm.LastMessageAt DESC", connection);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var flagType = reader.IsDBNull(3) ? MailFlagTypes.Active : reader.GetString(3);
            var isMine = !reader.IsDBNull(4) && reader.GetInt32(4) == 1;
            var hasAssignment = !reader.IsDBNull(5) && reader.GetInt32(5) == 1;

            var folder = flagType == MailFlagTypes.Closed ? MailFolders.Closed
                : flagType == MailFlagTypes.Spam ? MailFolders.Spam
                : isMine ? MailFolders.Mine
                : hasAssignment ? MailFolders.Assigned
                : MailFolders.Unassigned;

            result.Add(new EngineerMessageDrawerItem
            {
                MailId = reader.GetInt32(0),
                Subject = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                FromName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Folder = folder,
                FolderLabel = MailFolders.All.FirstOrDefault(f => f.Key == folder).Label ?? folder,
                LastMessageAt = reader.GetFieldValue<DateTimeOffset>(6)
            });
        }

        return result;
    }

    public async Task<List<EngineerCardEventDrawerItem>> GetCardEventsAsync(string userId, IEnumerable<string> statKeys)
    {
        var keys = statKeys.ToList();
        var result = new List<EngineerCardEventDrawerItem>();
        if (keys.Count == 0)
        {
            return result;
        }

        using var connection = CreateConnection();
        await connection.OpenAsync();

        var paramNames = keys.Select((_, i) => $"@Key{i}").ToList();

        using var command = new SqlCommand($@"
            SELECT e.BoardId, b.Title, e.EntityId, c.Title, e.StatKey, e.EventAt
            FROM dbo.EngineerStatEvents e
            INNER JOIN dbo.Boards b ON b.Id = e.BoardId
            INNER JOIN dbo.BoardCards c ON c.Id = e.EntityId
            WHERE e.UserId = @UserId AND e.StatKey IN ({string.Join(",", paramNames)}) AND {ActiveEventClause.Replace("SeenAt", "e.SeenAt")}
            ORDER BY e.EventAt DESC", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        for (var i = 0; i < keys.Count; i++)
        {
            command.Parameters.AddWithValue(paramNames[i], keys[i]);
        }

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new EngineerCardEventDrawerItem
            {
                BoardId = reader.GetInt32(0),
                BoardTitle = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                CardId = reader.GetInt32(2),
                CardTitle = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                StatKey = reader.GetString(4),
                EventAt = reader.GetFieldValue<DateTimeOffset>(5)
            });
        }

        return result;
    }

    public async Task MarkAllStatEventsSeenAsync(string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.EngineerStatEvents SET SeenAt = @Now WHERE UserId = @UserId AND SeenAt IS NULL", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    public async Task LogStatEventAsync(string userId, string statKey, string entityType, int entityId, int? boardId, DateTimeOffset eventAt)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.EngineerStatEvents (UserId, StatKey, EntityType, EntityId, BoardId, EventAt)
            VALUES (@UserId, @StatKey, @EntityType, @EntityId, @BoardId, @EventAt)", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@StatKey", statKey);
        command.Parameters.AddWithValue("@EntityType", entityType);
        command.Parameters.AddWithValue("@EntityId", entityId);
        command.Parameters.AddWithValue("@BoardId", (object?)boardId ?? DBNull.Value);
        command.Parameters.AddWithValue("@EventAt", eventAt);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetOtherEmployeeUserIdsAsync(string excludeUserId1, string? excludeUserId2)
    {
        var result = new List<string>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT ur.UserId
            FROM dbo.AspNetUserRoles ur
            INNER JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE r.Name = 'Employee' AND ur.UserId <> @Exclude1
              AND (@Exclude2 IS NULL OR ur.UserId <> @Exclude2)", connection);
        command.Parameters.AddWithValue("@Exclude1", excludeUserId1);
        command.Parameters.AddWithValue("@Exclude2", (object?)excludeUserId2 ?? DBNull.Value);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public async Task<List<EngineerNote>> GetNotesAsync(string userId)
    {
        var result = new List<EngineerNote>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT Id, UserId, Body, CreatedAt, SortOrder FROM dbo.EngineerNotes WHERE UserId = @UserId ORDER BY SortOrder ASC, CreatedAt DESC", connection);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new EngineerNote
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetString(1),
                Body = reader.GetString(2),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                SortOrder = reader.GetInt32(4)
            });
        }

        return result;
    }

    public async Task<EngineerNote> AddNoteAsync(string userId, string body)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        var now = DateTimeOffset.UtcNow;

        // Yeni not en uste (mevcut en kucuk SortOrder'in bir eksigine) eklenir,
        // boylece surukle-birakla elle siralanmis notlarin arasina karismaz.
        int newSortOrder;
        using (var minCommand = new SqlCommand(
            "SELECT MIN(SortOrder) FROM dbo.EngineerNotes WHERE UserId = @UserId", connection))
        {
            minCommand.Parameters.AddWithValue("@UserId", userId);
            var value = await minCommand.ExecuteScalarAsync();
            newSortOrder = value is int min ? min - 1 : 0;
        }

        using var command = new SqlCommand(@"
            INSERT INTO dbo.EngineerNotes (UserId, Body, CreatedAt, SortOrder)
            OUTPUT INSERTED.Id
            VALUES (@UserId, @Body, @Now, @SortOrder)", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Body", body);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@SortOrder", newSortOrder);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return new EngineerNote { Id = id, UserId = userId, Body = body, CreatedAt = now, SortOrder = newSortOrder };
    }

    public async Task DeleteNoteAsync(int noteId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "DELETE FROM dbo.EngineerNotes WHERE Id = @Id AND UserId = @UserId", connection);
        command.Parameters.AddWithValue("@Id", noteId);
        command.Parameters.AddWithValue("@UserId", userId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<EngineerNote?> UpdateNoteAsync(int noteId, string userId, string body)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();   

        DateTimeOffset createdAt;
        int sortOrder;
        using (var command = new SqlCommand(@"
            UPDATE dbo.EngineerNotes SET Body = @Body
            OUTPUT INSERTED.CreatedAt, INSERTED.SortOrder
            WHERE Id = @Id AND UserId = @UserId", connection))
        {
            command.Parameters.AddWithValue("@Id", noteId);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Body", body);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }
            createdAt = reader.GetFieldValue<DateTimeOffset>(0);
            sortOrder = reader.GetInt32(1);
        }

        return new EngineerNote { Id = noteId, UserId = userId, Body = body, CreatedAt = createdAt, SortOrder = sortOrder };
    }

    public async Task ReorderNotesAsync(string userId, IReadOnlyList<int> orderedNoteIds)
    {
        if (orderedNoteIds.Count == 0)
        {
            return;
        }

        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            for (var i = 0; i < orderedNoteIds.Count; i++)
            {
                using var command = new SqlCommand(
                    "UPDATE dbo.EngineerNotes SET SortOrder = @SortOrder WHERE Id = @Id AND UserId = @UserId", connection, transaction);
                command.Parameters.AddWithValue("@SortOrder", i);
                command.Parameters.AddWithValue("@Id", orderedNoteIds[i]);
                command.Parameters.AddWithValue("@UserId", userId);
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
}
