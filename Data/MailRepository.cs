using Microsoft.Data.SqlClient;
using task_list.Models;

namespace task_list.Data;

public class MailRepository : IMailRepository
{
    private readonly string _connectionString;

    public MailRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string is not configured.");
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    private const string SelectMailColumns = @"
        m.Id, m.ImapUid, m.MessageId, m.FromAddress, m.FromName, m.Subject,
        m.BodyText, m.BodyHtml, m.ReceivedAt, m.IsRead,
        m.FlagType, m.FlagSetByUserId, m.FlagSetAt, fu.DisplayName AS FlagSetByDisplayName,
        m.CategoryColorKey,
        m.DraftBody, m.DraftUpdatedByUserId, du.DisplayName AS DraftUpdatedByDisplayName, m.DraftUpdatedAt,
        (SELECT STRING_AGG(CONCAT(ma.UserId, N'||', ISNULL(mau.DisplayName, N''), N'||', CONVERT(nvarchar(40), ma.AssignedAt, 127)), N';;')
         FROM dbo.MailAssignments ma
         LEFT JOIN dbo.AspNetUsers mau ON mau.Id = ma.UserId
         WHERE ma.MailId = m.Id) AS AssignedUsersAgg,
        m.TaskName,
        (SELECT STRING_AGG(CONCAT(CONVERT(nvarchar(20), t.Id), N'||', t.Text, N'||', t.ColorKey), N';;')
         FROM dbo.MailTags t
         WHERE t.MailId = m.Id) AS TagsAgg";

    private const string SelectMailFrom = @"
        FROM dbo.Mails m
        LEFT JOIN dbo.AspNetUsers fu ON fu.Id = m.FlagSetByUserId
        LEFT JOIN dbo.AspNetUsers du ON du.Id = m.DraftUpdatedByUserId";

    /// <param name="onlyMine">
    /// Klasore ozgu "Sadece Benim" filtresi: Kapatilmis'ta gorev SIZIN UZERINIZDE
    /// (maile atanmissiniz), Gonderilenler'de yaniti SIZ gonderdiniz.
    /// </param>
    private static string BuildFolderWhereClause(string folder, bool onlyMine)
    {
        const string assignedToCurrentUser = "EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id AND ma.UserId = @UserId)";
        const string hasAnyAssignment = "EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id)";
        const string notClosedOrSpam = "m.FlagType NOT IN ('closed', 'spam')";

        return folder switch
        {
            MailFolders.Mine => $"{assignedToCurrentUser} AND {notClosedOrSpam}",
            MailFolders.Assigned => $"{hasAnyAssignment} AND {notClosedOrSpam}",
            // "Sadece Benim" burada gorevin SAHIBINI sorar, bayragi kimin koydugunu
            // degil: bir gorevi baska bir mühendis kapatmis olsa bile gorev sizin
            // uzerinizdeyse listede kalmali.
            MailFolders.Closed => "m.FlagType = 'closed'" + (onlyMine ? $" AND {assignedToCurrentUser}" : ""),
            MailFolders.Spam => "m.FlagType = 'spam'",
            MailFolders.Sent => "m.FlagType <> 'spam' AND EXISTS (SELECT 1 FROM dbo.MailReplies r WHERE r.MailMessageId = m.Id"
                + (onlyMine ? " AND r.SentByUserId = @UserId" : "") + ")",
            MailFolders.Drafts => "m.FlagType <> 'spam' AND m.DraftBody IS NOT NULL AND LEN(m.DraftBody) > 0",
            MailFolders.Archive => "EXISTS (SELECT 1 FROM dbo.MailArchives ar WHERE ar.MailId = m.Id AND ar.UserId = @UserId)",
            _ => $"NOT {hasAnyAssignment} AND {notClosedOrSpam}"
        };
    }

    public async Task<List<MailMessageModel>> GetByFolderAsync(string folder, string currentUserId, bool onlyMine = false)
    {
        var whereClause = BuildFolderWhereClause(folder, onlyMine);

        var result = new List<MailMessageModel>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand($@"
            SELECT {SelectMailColumns}
            {SelectMailFrom}
            WHERE {whereClause}
            ORDER BY m.ReceivedAt DESC", connection);
        command.Parameters.AddWithValue("@UserId", currentUserId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapMail(reader));
        }

        return result;
    }

    public async Task<List<int>> GetOrderedMailIdsAsync(string folder, string currentUserId, bool onlyMine = false)
    {
        var whereClause = BuildFolderWhereClause(folder, onlyMine);

        var result = new List<int>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand($@"
            SELECT m.Id
            FROM dbo.Mails m
            WHERE {whereClause}
            ORDER BY m.ReceivedAt DESC", connection);
        command.Parameters.AddWithValue("@UserId", currentUserId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    public async Task<List<int>> GetCustomFolderOrderedMailIdsAsync(int folderId, string userId)
    {
        var result = new List<int>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT m.Id
            FROM dbo.Mails m
            WHERE EXISTS (
                SELECT 1 FROM dbo.MailFolderItems fi
                INNER JOIN dbo.MailUserFolders f ON f.Id = fi.FolderId
                WHERE fi.FolderId = @FolderId AND fi.MailId = m.Id AND f.UserId = @UserId)
            ORDER BY m.ReceivedAt DESC", connection);
        command.Parameters.AddWithValue("@FolderId", folderId);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    public async Task<Dictionary<string, int>> GetFolderCountsAsync(string currentUserId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT
                SUM(CASE WHEN ca.HasAssignment = 0 AND m.FlagType NOT IN ('closed', 'spam') THEN 1 ELSE 0 END) AS UnassignedCount,
                SUM(CASE WHEN ca.IsMine = 1 AND m.FlagType NOT IN ('closed', 'spam') THEN 1 ELSE 0 END) AS MineCount,
                SUM(CASE WHEN ca.HasAssignment = 1 AND m.FlagType NOT IN ('closed', 'spam') THEN 1 ELSE 0 END) AS AssignedCount,
                SUM(CASE WHEN m.FlagType = 'closed' THEN 1 ELSE 0 END) AS ClosedCount,
                SUM(CASE WHEN m.FlagType = 'spam' THEN 1 ELSE 0 END) AS SpamCount,
                SUM(CASE WHEN m.FlagType <> 'spam' AND m.DraftBody IS NOT NULL AND LEN(m.DraftBody) > 0 THEN 1 ELSE 0 END) AS DraftsCount,
                (SELECT COUNT(DISTINCT r.MailMessageId)
                 FROM dbo.MailReplies r
                 INNER JOIN dbo.Mails m2 ON m2.Id = r.MailMessageId
                 WHERE m2.FlagType <> 'spam') AS SentCount,
                (SELECT COUNT(*) FROM dbo.MailArchives ar WHERE ar.UserId = @UserId) AS ArchiveCount
            FROM dbo.Mails m
            CROSS APPLY (
                SELECT
                    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id) THEN 1 ELSE 0 END AS BIT) AS HasAssignment,
                    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.MailAssignments ma WHERE ma.MailId = m.Id AND ma.UserId = @UserId) THEN 1 ELSE 0 END AS BIT) AS IsMine
            ) ca", connection);
        command.Parameters.AddWithValue("@UserId", currentUserId);

        using var reader = await command.ExecuteReaderAsync();
        var counts = new Dictionary<string, int>();
        if (await reader.ReadAsync())
        {
            counts[MailFolders.Unassigned] = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            counts[MailFolders.Mine] = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            counts[MailFolders.Assigned] = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            counts[MailFolders.Closed] = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            counts[MailFolders.Spam] = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            counts[MailFolders.Drafts] = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
            counts[MailFolders.Sent] = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
            counts[MailFolders.Archive] = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
        }

        return counts;
    }

    public async Task<MailMessageModel?> GetByIdAsync(int id)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        MailMessageModel? mail;

        using (var command = new SqlCommand($@"
            SELECT {SelectMailColumns}
            {SelectMailFrom}
            WHERE m.Id = @Id", connection))
        {
            command.Parameters.AddWithValue("@Id", id);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            mail = MapMail(reader);
        }

        using (var command = new SqlCommand(@"
            SELECT Id, MailMessageId, FileName, FilePath, ContentType, IsImage
            FROM dbo.MailAttachments
            WHERE MailMessageId = @MailMessageId
            ORDER BY Id", connection))
        {
            command.Parameters.AddWithValue("@MailMessageId", id);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                mail.Attachments.Add(new MailAttachment
                {
                    Id = reader.GetInt32(0),
                    MailMessageId = reader.GetInt32(1),
                    FileName = reader.GetString(2),
                    FilePath = reader.GetString(3),
                    ContentType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    IsImage = reader.GetBoolean(5)
                });
            }
        }

        using (var command = new SqlCommand(@"
            SELECT r.Id, r.MailMessageId, r.Body, r.SentByUserId, u.DisplayName AS SentByDisplayName, r.SentAt,
                   r.IsInbound, r.FromAddress, r.FromName
            FROM dbo.MailReplies r
            LEFT JOIN dbo.AspNetUsers u ON u.Id = r.SentByUserId
            WHERE r.MailMessageId = @MailMessageId
            ORDER BY r.SentAt ASC", connection))
        {
            command.Parameters.AddWithValue("@MailMessageId", id);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var isInbound = reader.GetBoolean(6);
                mail.Replies.Add(new MailReply
                {
                    Id = reader.GetInt32(0),
                    MailMessageId = reader.GetInt32(1),
                    Body = reader.GetString(2),
                    SentByUserId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    SentByDisplayName = isInbound
                        ? (reader.IsDBNull(8) ? (reader.IsDBNull(7) ? string.Empty : reader.GetString(7)) : reader.GetString(8))
                        : (reader.IsDBNull(4) ? string.Empty : reader.GetString(4)),
                    SentAt = reader.GetFieldValue<DateTimeOffset>(5),
                    IsInbound = isInbound,
                    FromAddress = reader.IsDBNull(7) ? null : reader.GetString(7),
                    FromName = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }
        }

        if (mail.Replies.Count > 0)
        {
            var repliesById = mail.Replies.ToDictionary(r => r.Id);

            using var command = new SqlCommand(@"
                SELECT ra.Id, ra.MailReplyId, ra.FileName, ra.FilePath, ra.ContentType, ra.IsImage
                FROM dbo.MailReplyAttachments ra
                INNER JOIN dbo.MailReplies r ON r.Id = ra.MailReplyId
                WHERE r.MailMessageId = @MailMessageId
                ORDER BY ra.Id", connection);
            command.Parameters.AddWithValue("@MailMessageId", id);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var replyId = reader.GetInt32(1);
                if (!repliesById.TryGetValue(replyId, out var reply))
                {
                    continue;
                }

                reply.Attachments.Add(new MailReplyAttachment
                {
                    Id = reader.GetInt32(0),
                    MailReplyId = replyId,
                    FileName = reader.GetString(2),
                    FilePath = reader.GetString(3),
                    ContentType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    IsImage = reader.GetBoolean(5)
                });
            }
        }

        return mail;
    }

    public async Task MarkAsReadAsync(int id)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.Mails SET IsRead = 1 WHERE Id = @Id AND IsRead = 0", connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task MarkAsUnreadAsync(int id)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.Mails SET IsRead = 0 WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<HashSet<string>> GetExistingImapUidsAsync()
    {
        var uids = new HashSet<string>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using (var command = new SqlCommand("SELECT ImapUid FROM dbo.Mails", connection))
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                uids.Add(reader.GetString(0));
            }
        }

        using (var command = new SqlCommand("SELECT ImapUid FROM dbo.MailReplies WHERE ImapUid IS NOT NULL", connection))
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                uids.Add(reader.GetString(0));
            }
        }

        // Uygulama icinde silinen maillerin UID'leri (bkz. DeleteMailAsync);
        // bunlar da "zaten var" sayilmazsa sync ayni maili posta kutusundan
        // tekrar tekrar cekip yeniden olusturur.
        using (var command = new SqlCommand("SELECT ImapUid FROM dbo.DeletedMailImapUids", connection))
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                uids.Add(reader.GetString(0));
            }
        }

        return uids;
    }

    public async Task<Dictionary<string, int>> GetMessageIdToMailIdMapAsync()
    {
        var map = new Dictionary<string, int>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT MessageId, Id FROM dbo.Mails WHERE MessageId IS NOT NULL AND LEN(MessageId) > 0", connection);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[reader.GetString(0)] = reader.GetInt32(1);
        }

        return map;
    }

    public async Task<int> InsertMailAsync(MailMessageModel mail)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.Mails
                (ImapUid, MessageId, FromAddress, FromName, Subject, BodyText, BodyHtml, ReceivedAt, IsRead)
            OUTPUT INSERTED.Id
            VALUES
                (@ImapUid, @MessageId, @FromAddress, @FromName, @Subject, @BodyText, @BodyHtml, @ReceivedAt, @IsRead)",
            connection);

        command.Parameters.AddWithValue("@ImapUid", mail.ImapUid);
        command.Parameters.AddWithValue("@MessageId", (object?)mail.MessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("@FromAddress", mail.FromAddress);
        command.Parameters.AddWithValue("@FromName", (object?)mail.FromName ?? DBNull.Value);
        command.Parameters.AddWithValue("@Subject", (object?)mail.Subject ?? DBNull.Value);
        command.Parameters.AddWithValue("@BodyText", (object?)mail.BodyText ?? DBNull.Value);
        command.Parameters.AddWithValue("@BodyHtml", (object?)mail.BodyHtml ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReceivedAt", mail.ReceivedAt);
        command.Parameters.AddWithValue("@IsRead", mail.IsRead);

        var id = await command.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task UpdateMailBodyHtmlAsync(int mailId, string bodyHtml)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.Mails SET BodyHtml = @BodyHtml WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", mailId);
        command.Parameters.AddWithValue("@BodyHtml", bodyHtml);
        await command.ExecuteNonQueryAsync();
    }

    public async Task InsertAttachmentAsync(int mailId, MailAttachment attachment)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.MailAttachments (MailMessageId, FileName, FilePath, ContentType, IsImage)
            VALUES (@MailMessageId, @FileName, @FilePath, @ContentType, @IsImage)", connection);

        command.Parameters.AddWithValue("@MailMessageId", mailId);
        command.Parameters.AddWithValue("@FileName", attachment.FileName);
        command.Parameters.AddWithValue("@FilePath", attachment.FilePath);
        command.Parameters.AddWithValue("@ContentType", (object?)attachment.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsImage", attachment.IsImage);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<MailFlagUpdateResult> SetFlagAsync(int mailId, string flagType, string userId, string userDisplayName)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            string? currentSetByUserId;

            using (var command = new SqlCommand(
                "SELECT FlagSetByUserId FROM dbo.Mails WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", mailId);
                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    transaction.Rollback();
                    return MailFlagUpdateResult.NotFound;
                }

                currentSetByUserId = reader.IsDBNull(0) ? null : reader.GetString(0);
            }

            // Atama durumu (bayrak kurallari icin) ayni kilitli islem icinde okunur;
            // boylece "atamayi kaldir + tamamlandi isaretle" yarisinda kural atlanamaz.
            var assignedUserIds = new List<string>();
            using (var assignedCommand = new SqlCommand(
                "SELECT UserId FROM dbo.MailAssignments WHERE MailId = @MailId", connection, transaction))
            {
                assignedCommand.Parameters.AddWithValue("@MailId", mailId);
                using var reader = await assignedCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    assignedUserIds.Add(reader.GetString(0));
                }
            }

            // Arayuzde bu secenekler zaten inaktif gosteriliyor (bkz. MailFlagPolicy);
            // burasi dogrudan POST atilarak atlatilmasini engelleyen sunucu tarafi kontrol.
            var hasAssignment = assignedUserIds.Count > 0;
            var isAssignedToCurrentUser = assignedUserIds.Any(id => string.Equals(id, userId, StringComparison.Ordinal));
            if (!MailFlagPolicy.IsAllowed(flagType, hasAssignment, isAssignedToCurrentUser))
            {
                transaction.Rollback();
                return hasAssignment
                    ? MailFlagUpdateResult.NotAllowedNotAssignee
                    : MailFlagUpdateResult.NotAllowedUnassigned;
            }

            // Bayragi baskasi koymussa degistirilemez -- ama bu kilit atama modeline
            // tabidir: goreve atanmis muhendisler birbirinin bayragini degistirebilir,
            // atanmamis mailde ise kimse bayragin sahibi sayilmaz (bkz. IsOwnerLockActive).
            if (MailFlagPolicy.IsOwnerLockActive(currentSetByUserId, userId, hasAssignment, isAssignedToCurrentUser))
            {
                transaction.Rollback();
                return MailFlagUpdateResult.LockedByOtherUser;
            }

            using (var updateCommand = new SqlCommand(@"
                UPDATE dbo.Mails
                SET FlagType = @FlagType, FlagSetByUserId = @UserId, FlagSetAt = @Now
                WHERE Id = @Id", connection, transaction))
            {
                updateCommand.Parameters.AddWithValue("@Id", mailId);
                updateCommand.Parameters.AddWithValue("@FlagType", flagType);
                updateCommand.Parameters.AddWithValue("@UserId", userId);
                updateCommand.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                await updateCommand.ExecuteNonQueryAsync();
            }

            if (flagType != MailFlagTypes.Closed)
            {
                // Istatistik kaydi sadece "Tamamlandi" icin uretilir.
                assignedUserIds.Clear();
            }

            transaction.Commit();

            // "Kapatılmış görevler" istatistigi: gorevin sahibi mühendisler (kapatan aktör haric).
            foreach (var assignedUserId in assignedUserIds.Where(id => !string.Equals(id, userId, StringComparison.Ordinal)))
            {
                await LogStatEventAsync(assignedUserId, EngineerStatKeys.MailClosed, "Mail", mailId, null, DateTimeOffset.UtcNow);
            }

            return MailFlagUpdateResult.Success;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<int> InsertReplyAsync(MailReply reply)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.MailReplies (MailMessageId, Body, SentByUserId, SentAt)
            OUTPUT INSERTED.Id
            VALUES (@MailMessageId, @Body, @SentByUserId, @SentAt)", connection);

        command.Parameters.AddWithValue("@MailMessageId", reply.MailMessageId);
        command.Parameters.AddWithValue("@Body", reply.Body);
        command.Parameters.AddWithValue("@SentByUserId", (object?)reply.SentByUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SentAt", reply.SentAt);

        var id = await command.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task<int> InsertInboundReplyAsync(MailReply reply)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.MailReplies (MailMessageId, Body, SentByUserId, SentAt, IsInbound, FromAddress, FromName, ImapUid)
            OUTPUT INSERTED.Id
            VALUES (@MailMessageId, @Body, NULL, @SentAt, 1, @FromAddress, @FromName, @ImapUid)", connection);

        command.Parameters.AddWithValue("@MailMessageId", reply.MailMessageId);
        command.Parameters.AddWithValue("@Body", reply.Body);
        command.Parameters.AddWithValue("@SentAt", reply.SentAt);
        command.Parameters.AddWithValue("@FromAddress", (object?)reply.FromAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("@FromName", (object?)reply.FromName ?? DBNull.Value);
        command.Parameters.AddWithValue("@ImapUid", (object?)reply.ImapUid ?? DBNull.Value);

        var id = await command.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task InsertReplyAttachmentAsync(int replyId, MailReplyAttachment attachment)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.MailReplyAttachments (MailReplyId, FileName, FilePath, ContentType, IsImage)
            VALUES (@MailReplyId, @FileName, @FilePath, @ContentType, @IsImage)", connection);

        command.Parameters.AddWithValue("@MailReplyId", replyId);
        command.Parameters.AddWithValue("@FileName", attachment.FileName);
        command.Parameters.AddWithValue("@FilePath", attachment.FilePath);
        command.Parameters.AddWithValue("@ContentType", (object?)attachment.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsImage", attachment.IsImage);

        await command.ExecuteNonQueryAsync();
    }

    public async Task AssignUsersAsync(int mailId, IEnumerable<string> userIds, string? actingUserId, string? actingUserDisplayName, string? taskName = null)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        if (!string.IsNullOrWhiteSpace(taskName))
        {
            using var taskNameCommand = new SqlCommand(
                "UPDATE dbo.Mails SET TaskName = @TaskName WHERE Id = @Id", connection);
            taskNameCommand.Parameters.AddWithValue("@Id", mailId);
            taskNameCommand.Parameters.AddWithValue("@TaskName", taskName.Trim());
            await taskNameCommand.ExecuteNonQueryAsync();
        }

        foreach (var userId in userIds.Distinct())
        {
            int rowsInserted;
            using (var command = new SqlCommand(@"
                INSERT INTO dbo.MailAssignments (MailId, UserId, AssignedAt, AssignedByUserId, AssignedByDisplayName)
                SELECT @MailId, @UserId, @Now, @ActingUserId, @ActingDisplayName
                WHERE NOT EXISTS (SELECT 1 FROM dbo.MailAssignments WHERE MailId = @MailId AND UserId = @UserId)", connection))
            {
                command.Parameters.AddWithValue("@MailId", mailId);
                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                command.Parameters.AddWithValue("@ActingUserId", (object?)actingUserId ?? DBNull.Value);
                command.Parameters.AddWithValue("@ActingDisplayName", (object?)actingUserDisplayName ?? DBNull.Value);
                rowsInserted = await command.ExecuteNonQueryAsync();
            }

            // "Yeni atananlar" istatistigi: sadece baskasi tarafindan atandiysa (kendi
            // "üzerime al" islemleri kendine bildirim uretmez).
            if (rowsInserted > 0 && actingUserId is not null && !string.Equals(actingUserId, userId, StringComparison.Ordinal))
            {
                await LogStatEventAsync(userId, EngineerStatKeys.MailAssignedToMe, "Mail", mailId, null, DateTimeOffset.UtcNow);
            }
        }
    }

    /// <summary>
    /// "İstatistiklerim" sayfasindaki decay'li sayaclar icin ortak olay kaydi
    /// (bkz. dbo.EngineerStatEvents / StatsRepository). Kendi baglantisini acar;
    /// cagiran islemin transaction'iyla atomik degildir (istatistik oldugu icin kabul edilebilir).
    /// </summary>
    private async Task LogStatEventAsync(string userId, string statKey, string entityType, int entityId, int? boardId, DateTimeOffset eventAt)
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

    public async Task<bool> RemoveAssignmentAsync(int mailId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "DELETE FROM dbo.MailAssignments WHERE MailId = @Id AND UserId = @UserId", connection);
        command.Parameters.AddWithValue("@Id", mailId);
        command.Parameters.AddWithValue("@UserId", userId);
        var affected = await command.ExecuteNonQueryAsync();

        return affected > 0;
    }

    public async Task SetCategoryMarkerAsync(int mailId, string? colorKey)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.Mails SET CategoryColorKey = @ColorKey WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", mailId);
        command.Parameters.AddWithValue("@ColorKey", (object?)colorKey ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SetTagsAsync(int mailId, IEnumerable<(string Text, string ColorKey)> tags, string? userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            using (var deleteCommand = new SqlCommand(
                "DELETE FROM dbo.MailTags WHERE MailId = @MailId", connection, transaction))
            {
                deleteCommand.Parameters.AddWithValue("@MailId", mailId);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag.Text))
                {
                    continue;
                }

                using var insertCommand = new SqlCommand(@"
                    INSERT INTO dbo.MailTags (MailId, Text, ColorKey, CreatedByUserId, CreatedAt)
                    VALUES (@MailId, @Text, @ColorKey, @UserId, @Now)", connection, transaction);
                insertCommand.Parameters.AddWithValue("@MailId", mailId);
                insertCommand.Parameters.AddWithValue("@Text", tag.Text.Trim());
                insertCommand.Parameters.AddWithValue("@ColorKey", tag.ColorKey);
                insertCommand.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
                insertCommand.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                await insertCommand.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task SaveDraftAsync(int mailId, string body, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            UPDATE dbo.Mails
            SET DraftBody = @Body, DraftUpdatedByUserId = @UserId, DraftUpdatedAt = @Now
            WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", mailId);
        command.Parameters.AddWithValue("@Body", body);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ClearDraftAsync(int mailId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            UPDATE dbo.Mails
            SET DraftBody = NULL, DraftUpdatedByUserId = NULL, DraftUpdatedAt = NULL
            WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", mailId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteMailAsync(int mailId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Silinen mailin (ve varsa yanitlarinin) IMAP UID'lerini kalici olarak
            // "mezar tasi" tablosuna kaydediyoruz; aksi halde bir sonraki
            // senkronizasyon ayni maili posta kutusundan tekrar cekip
            // "yeniden geliyormus" gibi yeniden olusturur (bkz. GetExistingImapUidsAsync).
            var uidsToTombstone = new List<string>();
            using (var command = new SqlCommand(
                "SELECT ImapUid FROM dbo.Mails WHERE Id = @Id", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", mailId);
                var result = await command.ExecuteScalarAsync();
                if (result is string mailUid)
                {
                    uidsToTombstone.Add(mailUid);
                }
            }

            using (var command = new SqlCommand(
                "SELECT ImapUid FROM dbo.MailReplies WHERE MailMessageId = @Id AND ImapUid IS NOT NULL", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", mailId);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    uidsToTombstone.Add(reader.GetString(0));
                }
            }

            foreach (var uid in uidsToTombstone)
            {
                using var tombstoneCommand = new SqlCommand(@"
                    IF NOT EXISTS (SELECT 1 FROM dbo.DeletedMailImapUids WHERE ImapUid = @ImapUid)
                    BEGIN
                        INSERT INTO dbo.DeletedMailImapUids (ImapUid, DeletedAt) VALUES (@ImapUid, @Now);
                    END", connection, transaction);
                tombstoneCommand.Parameters.AddWithValue("@ImapUid", uid);
                tombstoneCommand.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                await tombstoneCommand.ExecuteNonQueryAsync();
            }

            // MailAttachments / MailReplies / MailReplyAttachments / MailTags FK'lari ON DELETE CASCADE
            // oldugu icin tek DELETE tum bagli kayitlari da temizler.
            using (var deleteCommand = new SqlCommand("DELETE FROM dbo.Mails WHERE Id = @Id", connection, transaction))
            {
                deleteCommand.Parameters.AddWithValue("@Id", mailId);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<MailDraftTemplate>> GetDraftTemplatesAsync(string currentUserId)
    {
        var result = new List<MailDraftTemplate>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT t.Id, t.Title, t.Body, t.CreatedByUserId, cu.DisplayName AS CreatedByDisplayName,
                   t.CreatedAt, uu.DisplayName AS UpdatedByDisplayName, t.UpdatedAt, t.IsPrivate
            FROM dbo.MailDraftTemplates t
            LEFT JOIN dbo.AspNetUsers cu ON cu.Id = t.CreatedByUserId
            LEFT JOIN dbo.AspNetUsers uu ON uu.Id = t.UpdatedByUserId
            WHERE t.IsPrivate = 0 OR t.CreatedByUserId = @UserId
            ORDER BY t.Title ASC", connection);
        command.Parameters.AddWithValue("@UserId", currentUserId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new MailDraftTemplate
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                Body = reader.GetString(2),
                CreatedByUserId = reader.GetString(3),
                CreatedByDisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
                UpdatedByDisplayName = reader.IsDBNull(6) ? null : reader.GetString(6),
                UpdatedAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                IsPrivate = reader.GetBoolean(8)
            });
        }

        return result;
    }

    public async Task<int> CreateDraftTemplateAsync(string title, string body, string userId, bool isPrivate)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.MailDraftTemplates (Title, Body, CreatedByUserId, CreatedAt, IsPrivate)
            OUTPUT INSERTED.Id
            VALUES (@Title, @Body, @UserId, @Now, @IsPrivate)", connection);
        command.Parameters.AddWithValue("@Title", title);
        command.Parameters.AddWithValue("@Body", body);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@IsPrivate", isPrivate);

        var id = await command.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task UpdateDraftTemplateAsync(int id, string title, string body, string userId, bool isPrivate)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            UPDATE dbo.MailDraftTemplates
            SET Title = @Title, Body = @Body, UpdatedByUserId = @UserId, UpdatedAt = @Now, IsPrivate = @IsPrivate
            WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Title", title);
        command.Parameters.AddWithValue("@Body", body);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@IsPrivate", isPrivate);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteDraftTemplateAsync(int id)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand("DELETE FROM dbo.MailDraftTemplates WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetAnnotationsAsync(int mailId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT StrokesJson FROM dbo.MailAnnotations WHERE MailId = @MailId AND UserId = @UserId", connection);
        command.Parameters.AddWithValue("@MailId", mailId);
        command.Parameters.AddWithValue("@UserId", userId);

        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    public async Task SaveAnnotationsAsync(int mailId, string userId, string strokesJson)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            MERGE dbo.MailAnnotations AS target
            USING (SELECT @MailId AS MailId, @UserId AS UserId) AS src
            ON target.MailId = src.MailId AND target.UserId = src.UserId
            WHEN MATCHED THEN
                UPDATE SET StrokesJson = @StrokesJson, UpdatedAt = @Now
            WHEN NOT MATCHED THEN
                INSERT (MailId, UserId, StrokesJson, UpdatedAt)
                VALUES (@MailId, @UserId, @StrokesJson, @Now);", connection);
        command.Parameters.AddWithValue("@MailId", mailId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@StrokesJson", strokesJson);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<MailUserFolder>> GetUserFoldersAsync(string userId)
    {
        var result = new List<MailUserFolder>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT f.Id, f.Name, COUNT(fi.MailId) AS ItemCount
            FROM dbo.MailUserFolders f
            LEFT JOIN dbo.MailFolderItems fi ON fi.FolderId = f.Id
            WHERE f.UserId = @UserId
            GROUP BY f.Id, f.Name
            ORDER BY f.Name ASC", connection);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new MailUserFolder
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Count = reader.GetInt32(2)
            });
        }

        return result;
    }

    public async Task<MailUserFolder?> GetUserFolderAsync(int folderId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT Id, Name FROM dbo.MailUserFolders WHERE Id = @Id AND UserId = @UserId", connection);
        command.Parameters.AddWithValue("@Id", folderId);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new MailUserFolder { Id = reader.GetInt32(0), Name = reader.GetString(1) };
    }

    public async Task<int> CreateUserFolderAsync(string userId, string name)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            INSERT INTO dbo.MailUserFolders (UserId, Name, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@UserId, @Name, @Now)", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);

        var id = await command.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task DeleteUserFolderAsync(int folderId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "DELETE FROM dbo.MailUserFolders WHERE Id = @Id AND UserId = @UserId", connection);
        command.Parameters.AddWithValue("@Id", folderId);
        command.Parameters.AddWithValue("@UserId", userId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<MailMessageModel>> GetByCustomFolderAsync(int folderId, string userId)
    {
        var result = new List<MailMessageModel>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand($@"
            SELECT {SelectMailColumns}
            {SelectMailFrom}
            WHERE EXISTS (
                SELECT 1 FROM dbo.MailFolderItems fi
                INNER JOIN dbo.MailUserFolders f ON f.Id = fi.FolderId
                WHERE fi.FolderId = @FolderId AND fi.MailId = m.Id AND f.UserId = @UserId)
            ORDER BY m.ReceivedAt DESC", connection);
        command.Parameters.AddWithValue("@FolderId", folderId);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapMail(reader));
        }

        return result;
    }

    public async Task<HashSet<int>> GetMailFolderIdsAsync(int mailId, string userId)
    {
        var result = new HashSet<int>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT fi.FolderId
            FROM dbo.MailFolderItems fi
            INNER JOIN dbo.MailUserFolders f ON f.Id = fi.FolderId
            WHERE fi.MailId = @MailId AND f.UserId = @UserId", connection);
        command.Parameters.AddWithValue("@MailId", mailId);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    public async Task SetMailFoldersAsync(int mailId, string userId, IEnumerable<int> folderIds)
    {
        var idList = folderIds.Distinct().ToList();

        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            using (var deleteCommand = new SqlCommand(@"
                DELETE fi FROM dbo.MailFolderItems fi
                INNER JOIN dbo.MailUserFolders f ON f.Id = fi.FolderId
                WHERE fi.MailId = @MailId AND f.UserId = @UserId", connection, transaction))
            {
                deleteCommand.Parameters.AddWithValue("@MailId", mailId);
                deleteCommand.Parameters.AddWithValue("@UserId", userId);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            foreach (var folderId in idList)
            {
                using var insertCommand = new SqlCommand(@"
                    INSERT INTO dbo.MailFolderItems (FolderId, MailId, AddedAt)
                    SELECT @FolderId, @MailId, @Now
                    WHERE EXISTS (SELECT 1 FROM dbo.MailUserFolders WHERE Id = @FolderId AND UserId = @UserId)", connection, transaction);
                insertCommand.Parameters.AddWithValue("@FolderId", folderId);
                insertCommand.Parameters.AddWithValue("@MailId", mailId);
                insertCommand.Parameters.AddWithValue("@UserId", userId);
                insertCommand.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                await insertCommand.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> IsArchivedAsync(int mailId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT 1 FROM dbo.MailArchives WHERE MailId = @MailId AND UserId = @UserId", connection);
        command.Parameters.AddWithValue("@MailId", mailId);
        command.Parameters.AddWithValue("@UserId", userId);

        var result = await command.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task SetArchivedAsync(int mailId, string userId, bool archived)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        if (archived)
        {
            using var command = new SqlCommand(@"
                INSERT INTO dbo.MailArchives (MailId, UserId, ArchivedAt)
                SELECT @MailId, @UserId, @Now
                WHERE NOT EXISTS (SELECT 1 FROM dbo.MailArchives WHERE MailId = @MailId AND UserId = @UserId)", connection);
            command.Parameters.AddWithValue("@MailId", mailId);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync();
        }
        else
        {
            using var command = new SqlCommand(
                "DELETE FROM dbo.MailArchives WHERE MailId = @MailId AND UserId = @UserId", connection);
            command.Parameters.AddWithValue("@MailId", mailId);
            command.Parameters.AddWithValue("@UserId", userId);
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task UpsertMailReplySeenAsync(int mailId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            MERGE dbo.MailReplySeen AS target
            USING (SELECT @MailId AS MailId, @UserId AS UserId) AS src
            ON target.MailId = src.MailId AND target.UserId = src.UserId
            WHEN MATCHED THEN
                UPDATE SET LastSeenAt = @Now
            WHEN NOT MATCHED THEN
                INSERT (MailId, UserId, LastSeenAt)
                VALUES (@MailId, @UserId, @Now);", connection);
        command.Parameters.AddWithValue("@MailId", mailId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private static MailMessageModel MapMail(SqlDataReader reader)
    {
        var mail = new MailMessageModel
        {
            Id = reader.GetInt32(0),
            ImapUid = reader.GetString(1),
            MessageId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            FromAddress = reader.GetString(3),
            FromName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            Subject = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            BodyText = reader.IsDBNull(6) ? null : reader.GetString(6),
            BodyHtml = reader.IsDBNull(7) ? null : reader.GetString(7),
            ReceivedAt = reader.GetFieldValue<DateTimeOffset>(8),
            IsRead = reader.GetBoolean(9),
            FlagType = reader.IsDBNull(10) ? MailFlagTypes.Active : reader.GetString(10),
            FlagSetByUserId = reader.IsDBNull(11) ? null : reader.GetString(11),
            FlagSetAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            FlagSetByDisplayName = reader.IsDBNull(13) ? null : reader.GetString(13),
            CategoryColorKey = reader.IsDBNull(14) ? null : reader.GetString(14),
            DraftBody = reader.IsDBNull(15) ? null : reader.GetString(15),
            DraftUpdatedByDisplayName = reader.IsDBNull(17) ? null : reader.GetString(17),
            DraftUpdatedAt = reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18)
        };

        if (!reader.IsDBNull(19))
        {
            mail.AssignedUsers = ParseAssignedUsersAgg(reader.GetString(19));
        }

        mail.TaskName = reader.IsDBNull(20) ? null : reader.GetString(20);

        if (!reader.IsDBNull(21))
        {
            mail.Tags = ParseTagsAgg(reader.GetString(21));
        }

        return mail;
    }

    private static List<MailAssignmentInfo> ParseAssignedUsersAgg(string agg)
    {
        var result = new List<MailAssignmentInfo>();
        foreach (var entry in agg.Split(";;", StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split("||");
            if (parts.Length != 3)
            {
                continue;
            }

            result.Add(new MailAssignmentInfo
            {
                UserId = parts[0],
                DisplayName = parts[1],
                AssignedAt = DateTimeOffset.TryParse(parts[2], out var assignedAt) ? assignedAt : default
            });
        }

        return result;
    }

    private static List<MailTagInfo> ParseTagsAgg(string agg)
    {
        var result = new List<MailTagInfo>();
        foreach (var entry in agg.Split(";;", StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split("||");
            if (parts.Length != 3 || !int.TryParse(parts[0], out var id))
            {
                continue;
            }

            result.Add(new MailTagInfo
            {
                Id = id,
                Text = parts[1],
                ColorKey = parts[2]
            });
        }

        return result;
    }
}
