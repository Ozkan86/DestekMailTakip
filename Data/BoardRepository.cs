using Microsoft.Data.SqlClient;
using task_list.Models;

namespace task_list.Data;

public class BoardRepository : IBoardRepository
{
    private readonly string _connectionString;

    public BoardRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string is not configured.");
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    private const string SelectCardColumns = @"
        c.Id, c.BoardId, c.ListKey, c.Title, c.Description,
        c.CreatedByUserId, c.CreatedByDisplayName, c.CreatedAt,
        c.AssignedUserId, au.DisplayName AS AssignedUserDisplayName, c.AssignedAt,
        c.MovedToTestAt, c.CompletedAt,
        c.LastRejectionNote, c.LastRejectedAt, c.RejectedCount, b.Title AS BoardTitle,
        c.CoverColor, c.CoverImagePath, c.SortOrder,
        c.IsArchived, c.ArchivedAt, c.ArchivedByUserId,
        c.SprintRound, c.ApprovalStatus, c.AssignedListKey";

    private const string SelectCardFrom = @"
        FROM dbo.BoardCards c
        LEFT JOIN dbo.AspNetUsers au ON au.Id = c.AssignedUserId
        INNER JOIN dbo.Boards b ON b.Id = c.BoardId";

    public async Task<int> CreateBoardAsync(string title, string ownerId, string ownerDisplayName, IEnumerable<string> authorizedEmails, string templateKey)
    {
        var resolvedTemplateKey = BoardTemplates.IsValidKey(templateKey) ? templateKey : BoardTemplates.Klasik;

        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            int boardId;
            using (var command = new SqlCommand(@"
                INSERT INTO dbo.Boards (Title, CreatedByUserId, CreatedByDisplayName, CreatedAt, TemplateKey)
                OUTPUT INSERTED.Id
                VALUES (@Title, @OwnerId, @OwnerDisplayName, @Now, @TemplateKey)", connection, transaction))
            {
                command.Parameters.AddWithValue("@Title", title);
                command.Parameters.AddWithValue("@OwnerId", ownerId);
                command.Parameters.AddWithValue("@OwnerDisplayName", ownerDisplayName);
                command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                command.Parameters.AddWithValue("@TemplateKey", resolvedTemplateKey);
                boardId = Convert.ToInt32(await command.ExecuteScalarAsync());
            }

            var normalizedEmails = authorizedEmails
                .Select(e => e.Trim().ToLowerInvariant())
                .Where(e => e.Length > 0)
                .Distinct()
                .ToList();

            foreach (var email in normalizedEmails)
            {
                using var command = new SqlCommand(@"
                    INSERT INTO dbo.BoardAuthorizedEmails (BoardId, Email, AddedAt)
                    VALUES (@BoardId, @Email, @Now)", connection, transaction);
                command.Parameters.AddWithValue("@BoardId", boardId);
                command.Parameters.AddWithValue("@Email", email);
                command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
            return boardId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<int> StartPreviewBoardAsync(string templateKey, string userId, string userDisplayName)
    {
        var template = BoardTemplates.Get(templateKey);

        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Ayni musteri ayni sablonu daha once onizlediyse (StartPreview
            // yarim kalmis/tekrar baslatilmis olabilir), eski onizleme panosunu
            // (ve ON DELETE CASCADE ile tum kartlarini/etiketlerini) sil; boylece
            // "yeniden baslatinca her sey sifirlanir" davranisi garanti edilir.
            using (var deleteCommand = new SqlCommand(
                "DELETE FROM dbo.Boards WHERE CreatedByUserId = @UserId AND TemplateKey = @TemplateKey AND IsPreview = 1",
                connection, transaction))
            {
                deleteCommand.Parameters.AddWithValue("@UserId", userId);
                deleteCommand.Parameters.AddWithValue("@TemplateKey", template.Key);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            int boardId;
            using (var command = new SqlCommand(@"
                INSERT INTO dbo.Boards (Title, CreatedByUserId, CreatedByDisplayName, CreatedAt, TemplateKey, IsPreview)
                OUTPUT INSERTED.Id
                VALUES (@Title, @OwnerId, @OwnerDisplayName, @Now, @TemplateKey, 1)", connection, transaction))
            {
                command.Parameters.AddWithValue("@Title", $"Önizleme: {template.Name}");
                command.Parameters.AddWithValue("@OwnerId", userId);
                command.Parameters.AddWithValue("@OwnerDisplayName", userDisplayName);
                command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                command.Parameters.AddWithValue("@TemplateKey", template.Key);
                boardId = Convert.ToInt32(await command.ExecuteScalarAsync());
            }

            foreach (var list in template.Lists)
            {
                var seedCards = BoardTemplatePreviewSeeds.GetCardsFor(template.Key, list.Key);
                for (var order = 0; order < seedCards.Length; order++)
                {
                    var seed = seedCards[order];
                    using var cardCommand = new SqlCommand(@"
                        INSERT INTO dbo.BoardCards (BoardId, ListKey, Title, Description, CreatedByUserId, CreatedByDisplayName, CreatedAt, RejectedCount, SortOrder, SprintRound)
                        VALUES (@BoardId, @ListKey, @Title, @Description, @CreatorId, @CreatorDisplayName, @Now, 0, @SortOrder, 1)", connection, transaction);
                    cardCommand.Parameters.AddWithValue("@BoardId", boardId);
                    cardCommand.Parameters.AddWithValue("@ListKey", list.Key);
                    cardCommand.Parameters.AddWithValue("@Title", seed.Title);
                    cardCommand.Parameters.AddWithValue("@Description", (object?)seed.Description ?? DBNull.Value);
                    cardCommand.Parameters.AddWithValue("@CreatorId", userId);
                    cardCommand.Parameters.AddWithValue("@CreatorDisplayName", "Şablon Önizleme");
                    cardCommand.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                    cardCommand.Parameters.AddWithValue("@SortOrder", order);
                    await cardCommand.ExecuteNonQueryAsync();
                }
            }

            transaction.Commit();
            return boardId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<string?> GetBoardTemplateKeyAsync(int boardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand("SELECT TemplateKey FROM dbo.Boards WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", boardId);

        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<List<Board>> GetBoardsForCustomerAsync(string userId, string normalizedEmail)
    {
        var result = new List<Board>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using (var command = new SqlCommand(@"
            SELECT DISTINCT b.Id, b.Title, b.CreatedByUserId, b.CreatedByDisplayName, b.CreatedAt, b.TodoColor, b.TestColor, b.DoneColor, b.TemplateKey, b.CurrentSprintRound, b.IsPreview
            FROM dbo.Boards b
            LEFT JOIN dbo.BoardAuthorizedEmails bae ON bae.BoardId = b.Id
            WHERE (b.CreatedByUserId = @UserId OR bae.Email = @Email) AND b.IsPreview = 0
            ORDER BY b.CreatedAt DESC", connection))
        {
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Email", normalizedEmail);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(MapBoard(reader));
            }
        }

        // Sadece "Yetkili Kullanıcı Ekle/Kaldır" paneli sahibine gosterildigi
        // icin, yetkili e-postalari yalnizca kullanicinin sahip oldugu panolar
        // icin (tek bir toplu sorguyla) yukluyoruz.
        var ownedBoardIds = result
            .Where(b => string.Equals(b.CreatedByUserId, userId, StringComparison.Ordinal))
            .Select(b => b.Id)
            .ToList();

        if (ownedBoardIds.Count > 0)
        {
            var boardsById = result.ToDictionary(b => b.Id);
            var paramNames = ownedBoardIds.Select((_, idx) => $"@Id{idx}").ToArray();

            using var command = new SqlCommand($@"
                SELECT BoardId, Email FROM dbo.BoardAuthorizedEmails
                WHERE BoardId IN ({string.Join(",", paramNames)})
                ORDER BY Email", connection);
            for (var i = 0; i < ownedBoardIds.Count; i++)
            {
                command.Parameters.AddWithValue(paramNames[i], ownedBoardIds[i]);
            }

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var boardId = reader.GetInt32(0);
                if (boardsById.TryGetValue(boardId, out var board))
                {
                    board.AuthorizedEmails.Add(reader.GetString(1));
                }
            }
        }

        return result;
    }

    public async Task<CustomerBoardSummary> GetCustomerBoardSummaryAsync(string userId, string normalizedEmail)
    {
        var boards = await GetBoardsForCustomerAsync(userId, normalizedEmail);
        var summary = new CustomerBoardSummary { TotalBoards = boards.Count };

        if (boards.Count == 0)
        {
            return summary;
        }

        var boardsById = boards.ToDictionary(b => b.Id);
        var activity = new List<BoardActivityItem>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        var paramNames = boards.Select((_, idx) => $"@Id{idx}").ToArray();
        using (var command = new SqlCommand($@"
            SELECT {SelectCardColumns}
            {SelectCardFrom}
            WHERE c.BoardId IN ({string.Join(",", paramNames)}) AND c.IsArchived = 0", connection))
        {
            for (var i = 0; i < boards.Count; i++)
            {
                command.Parameters.AddWithValue(paramNames[i], boards[i].Id);
            }

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var card = MapCard(reader);
                var board = boardsById[card.BoardId];
                var template = board.Template;

                if (!IsCardInFinalState(card, template))
                {
                    summary.OpenCardCount++;
                }

                if (IsPendingCustomerApproval(card, template))
                {
                    summary.PendingApprovalCount++;
                }

                var (timestamp, actionLabel) = GetCardLastActivity(card);
                activity.Add(new BoardActivityItem
                {
                    BoardId = board.Id,
                    BoardTitle = board.Title,
                    CardTitle = card.Title,
                    ListLabel = template.GetList(card.ListKey)?.Label ?? card.ListKey,
                    ActionLabel = actionLabel,
                    Timestamp = timestamp
                });
            }
        }

        summary.RecentActivity = activity
            .OrderByDescending(a => a.Timestamp)
            .Take(5)
            .ToList();

        return summary;
    }

    /// <summary>
    /// Kart, sablonunun son (bitis) listesine ulasmis mi? Sprint turlu sablonda
    /// bu, Sprint Done listesinde olup tek tek Onaylandi durumuna gelmis olmak
    /// anlamina gelir (liste degismiyor, sadece ApprovalStatus degisiyor).
    /// </summary>
    private static bool IsCardInFinalState(BoardCard card, BoardTemplateDefinition template)
    {
        if (template.HasSprintRounds)
        {
            return string.Equals(card.ListKey, "sprint-done", StringComparison.Ordinal) &&
                string.Equals(card.ApprovalStatus, "Approved", StringComparison.Ordinal);
        }

        var finalListKey = template.Lists.Count > 0 ? template.Lists[^1].Key : BoardLists.Done;
        return string.Equals(card.ListKey, finalListKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Kart, musterinin onay/red vermesi beklenen bir listede mi (Klasik'te
    /// sabit "test" listesi; sprint sablonunda ApprovalStatus henuz atanmamis
    /// Sprint Done kartlari; diger jenerik sablonlarda Musteri rolune acik
    /// gecislerin kaynak listeleri)?
    /// </summary>
    private static bool IsPendingCustomerApproval(BoardCard card, BoardTemplateDefinition template)
    {
        if (template.Key == BoardTemplates.Klasik)
        {
            return string.Equals(card.ListKey, BoardLists.Test, StringComparison.Ordinal);
        }

        if (template.HasSprintRounds)
        {
            return string.Equals(card.ListKey, "sprint-done", StringComparison.Ordinal) && card.ApprovalStatus is null;
        }

        return template.Transitions.Any(t =>
            t.AllowedRole == BoardAddCardRole.Customer &&
            t.FromListKey != "*" &&
            string.Equals(t.FromListKey, card.ListKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// Kartin en son ne zaman "hareket ettigini" (Klasik'e ozel MovedToTestAt/
    /// CompletedAt/LastRejectedAt damgalarindan en yenisi, yoksa olusturulma
    /// zamani) ve bu hareketin kisa etiketini doner. Jenerik sablonlarda henuz
    /// tasima zaman damgasi tutulmadigindan bu kartlar icin CreatedAt kullanilir.
    /// </summary>
    private static (DateTimeOffset Timestamp, string ActionLabel) GetCardLastActivity(BoardCard card)
    {
        var timestamp = card.CreatedAt;
        var label = "Yeni eklendi";

        if (card.MovedToTestAt is { } movedAt && movedAt > timestamp)
        {
            timestamp = movedAt;
            label = "Teste taşındı";
        }

        if (card.LastRejectedAt is { } rejectedAt && rejectedAt > timestamp)
        {
            timestamp = rejectedAt;
            label = "Reddedildi";
        }

        if (card.CompletedAt is { } completedAt && completedAt > timestamp)
        {
            timestamp = completedAt;
            label = "Tamamlandı";
        }

        return (timestamp, label);
    }

    public async Task<List<Board>> GetAllBoardsAsync()
    {
        var result = new List<Board>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT Id, Title, CreatedByUserId, CreatedByDisplayName, CreatedAt, TodoColor, TestColor, DoneColor, TemplateKey, CurrentSprintRound, IsPreview
            FROM dbo.Boards
            WHERE IsPreview = 0
            ORDER BY CreatedAt DESC", connection);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapBoard(reader));
        }

        return result;
    }

    public async Task<Board?> GetBoardDetailsAsync(int boardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        Board? board;
        using (var command = new SqlCommand(
            "SELECT Id, Title, CreatedByUserId, CreatedByDisplayName, CreatedAt, TodoColor, TestColor, DoneColor, TemplateKey, CurrentSprintRound, IsPreview FROM dbo.Boards WHERE Id = @Id", connection))
        {
            command.Parameters.AddWithValue("@Id", boardId);
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            board = MapBoard(reader);
        }

        using (var command = new SqlCommand(
            "SELECT Email FROM dbo.BoardAuthorizedEmails WHERE BoardId = @BoardId ORDER BY Email", connection))
        {
            command.Parameters.AddWithValue("@BoardId", boardId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                board.AuthorizedEmails.Add(reader.GetString(0));
            }
        }

        using (var command = new SqlCommand(
            "SELECT ListKey, Color FROM dbo.BoardListColors WHERE BoardId = @BoardId", connection))
        {
            command.Parameters.AddWithValue("@BoardId", boardId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                board.ListColors[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var cardLabels = new Dictionary<int, List<BoardLabel>>();
        using (var command = new SqlCommand(
            // Panodaki kartlarin uzerinde yalnizca SECILI etiketler gosterilir.
            "SELECT Id, BoardId, CardId, Name, Color, IsSelected FROM dbo.BoardLabels WHERE BoardId = @BoardId AND CardId IS NOT NULL AND IsSelected = 1 ORDER BY Id", connection))
        {
            command.Parameters.AddWithValue("@BoardId", boardId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var label = MapLabel(reader);
                if (!cardLabels.TryGetValue(label.CardId, out var list))
                {
                    list = new List<BoardLabel>();
                    cardLabels[label.CardId] = list;
                }
                list.Add(label);
            }
        }

        using (var command = new SqlCommand($@"
            SELECT {SelectCardColumns}
            {SelectCardFrom}
            WHERE c.BoardId = @BoardId AND c.IsArchived = 0
            ORDER BY c.ListKey ASC, c.SortOrder ASC", connection))
        {
            command.Parameters.AddWithValue("@BoardId", boardId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var card = MapCard(reader);
                if (cardLabels.TryGetValue(card.Id, out var labels))
                {
                    card.Labels = labels;
                }
                board.Cards.Add(card);
            }
        }

        return board;
    }

    public async Task<bool> IsCustomerAuthorizedAsync(int boardId, string userId, string normalizedEmail)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT 1
            FROM dbo.Boards b
            LEFT JOIN dbo.BoardAuthorizedEmails bae ON bae.BoardId = b.Id
            WHERE b.Id = @BoardId AND (b.CreatedByUserId = @UserId OR bae.Email = @Email)", connection);
        command.Parameters.AddWithValue("@BoardId", boardId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Email", normalizedEmail);

        var result = await command.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task DeleteBoardAsync(int boardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand("DELETE FROM dbo.Boards WHERE Id = @BoardId", connection);
        command.Parameters.AddWithValue("@BoardId", boardId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeletePreviewBoardAsync(int boardId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "DELETE FROM dbo.Boards WHERE Id = @BoardId AND CreatedByUserId = @UserId AND IsPreview = 1", connection);
        command.Parameters.AddWithValue("@BoardId", boardId);
        command.Parameters.AddWithValue("@UserId", userId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task RenameBoardAsync(int boardId, string title)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand("UPDATE dbo.Boards SET Title = @Title WHERE Id = @BoardId", connection);
        command.Parameters.AddWithValue("@BoardId", boardId);
        command.Parameters.AddWithValue("@Title", title);
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddAuthorizedEmailsAsync(int boardId, IEnumerable<string> emails)
    {
        var normalizedEmails = emails
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();

        if (normalizedEmails.Count == 0)
        {
            return;
        }

        using var connection = CreateConnection();
        await connection.OpenAsync();

        foreach (var email in normalizedEmails)
        {
            using var existsCommand = new SqlCommand(
                "SELECT 1 FROM dbo.BoardAuthorizedEmails WHERE BoardId = @BoardId AND Email = @Email", connection);
            existsCommand.Parameters.AddWithValue("@BoardId", boardId);
            existsCommand.Parameters.AddWithValue("@Email", email);
            var exists = await existsCommand.ExecuteScalarAsync() is not null;
            if (exists)
            {
                continue;
            }

            using var insertCommand = new SqlCommand(@"
                INSERT INTO dbo.BoardAuthorizedEmails (BoardId, Email, AddedAt)
                VALUES (@BoardId, @Email, @Now)", connection);
            insertCommand.Parameters.AddWithValue("@BoardId", boardId);
            insertCommand.Parameters.AddWithValue("@Email", email);
            insertCommand.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            await insertCommand.ExecuteNonQueryAsync();
        }
    }

    public async Task RemoveAuthorizedEmailAsync(int boardId, string email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return;
        }

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "DELETE FROM dbo.BoardAuthorizedEmails WHERE BoardId = @BoardId AND Email = @Email", connection);
        command.Parameters.AddWithValue("@BoardId", boardId);
        command.Parameters.AddWithValue("@Email", normalized);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SetListColorAsync(int boardId, string listKey, string? color)
    {
        var columnName = listKey switch
        {
            BoardLists.Todo => "TodoColor",
            BoardLists.Test => "TestColor",
            BoardLists.Done => "DoneColor",
            _ => null
        };

        if (columnName is null)
        {
            return;
        }

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            $"UPDATE dbo.Boards SET {columnName} = @Color WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", boardId);
        command.Parameters.AddWithValue("@Color", (object?)color ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SetGenericListColorAsync(int boardId, string listKey, string? color)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        if (color is null)
        {
            using var deleteCommand = new SqlCommand(
                "DELETE FROM dbo.BoardListColors WHERE BoardId = @BoardId AND ListKey = @ListKey", connection);
            deleteCommand.Parameters.AddWithValue("@BoardId", boardId);
            deleteCommand.Parameters.AddWithValue("@ListKey", listKey);
            await deleteCommand.ExecuteNonQueryAsync();
            return;
        }

        using var upsertCommand = new SqlCommand(@"
            UPDATE dbo.BoardListColors SET Color = @Color WHERE BoardId = @BoardId AND ListKey = @ListKey;
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.BoardListColors (BoardId, ListKey, Color) VALUES (@BoardId, @ListKey, @Color);
            END", connection);
        upsertCommand.Parameters.AddWithValue("@BoardId", boardId);
        upsertCommand.Parameters.AddWithValue("@ListKey", listKey);
        upsertCommand.Parameters.AddWithValue("@Color", color);
        await upsertCommand.ExecuteNonQueryAsync();
    }

    public async Task<int> AddCardAsync(int boardId, string title, string? description, string creatorId, string creatorDisplayName)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var nextOrder = await GetListCountAsync(connection, transaction, boardId, BoardLists.Todo);

            using var command = new SqlCommand(@"
                INSERT INTO dbo.BoardCards (BoardId, ListKey, Title, Description, CreatedByUserId, CreatedByDisplayName, CreatedAt, RejectedCount, SortOrder)
                OUTPUT INSERTED.Id
                VALUES (@BoardId, @ListKey, @Title, @Description, @CreatorId, @CreatorDisplayName, @Now, 0, @SortOrder)", connection, transaction);
            command.Parameters.AddWithValue("@BoardId", boardId);
            command.Parameters.AddWithValue("@ListKey", BoardLists.Todo);
            command.Parameters.AddWithValue("@Title", title);
            command.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            command.Parameters.AddWithValue("@CreatorId", creatorId);
            command.Parameters.AddWithValue("@CreatorDisplayName", creatorDisplayName);
            command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("@SortOrder", nextOrder);

            var id = await command.ExecuteScalarAsync();
            transaction.Commit();
            return Convert.ToInt32(id);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<int> AddCardToListAsync(int boardId, string listKey, string title, string? description, string creatorId, string creatorDisplayName)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            int sprintRound = 1;
            using (var boardCommand = new SqlCommand(
                "SELECT CurrentSprintRound FROM dbo.Boards WHERE Id = @BoardId", connection, transaction))
            {
                boardCommand.Parameters.AddWithValue("@BoardId", boardId);
                var result = await boardCommand.ExecuteScalarAsync();
                if (result is int round)
                {
                    sprintRound = round;
                }
            }

            var nextOrder = await GetListCountAsync(connection, transaction, boardId, listKey);

            using var command = new SqlCommand(@"
                INSERT INTO dbo.BoardCards (BoardId, ListKey, Title, Description, CreatedByUserId, CreatedByDisplayName, CreatedAt, RejectedCount, SortOrder, SprintRound)
                OUTPUT INSERTED.Id
                VALUES (@BoardId, @ListKey, @Title, @Description, @CreatorId, @CreatorDisplayName, @Now, 0, @SortOrder, @SprintRound)", connection, transaction);
            command.Parameters.AddWithValue("@BoardId", boardId);
            command.Parameters.AddWithValue("@ListKey", listKey);
            command.Parameters.AddWithValue("@Title", title);
            command.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            command.Parameters.AddWithValue("@CreatorId", creatorId);
            command.Parameters.AddWithValue("@CreatorDisplayName", creatorDisplayName);
            command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("@SortOrder", nextOrder);
            command.Parameters.AddWithValue("@SprintRound", sprintRound);

            var id = await command.ExecuteScalarAsync();
            transaction.Commit();
            return Convert.ToInt32(id);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<BoardMoveResult> MoveCardWithTransitionAsync(int cardId, int boardId, string targetListKey, string actingUserId, bool isEngineer, bool isAdmin, string? note)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            string currentListKey;
            string templateKey;
            string? assignedUserId;
            using (var command = new SqlCommand(@"
                SELECT c.ListKey, b.TemplateKey, c.AssignedUserId
                FROM dbo.BoardCards c
                INNER JOIN dbo.Boards b ON b.Id = c.BoardId
                WHERE c.Id = @Id AND c.BoardId = @BoardId", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", cardId);
                command.Parameters.AddWithValue("@BoardId", boardId);
                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    transaction.Rollback();
                    return BoardMoveResult.NotFound;
                }
                currentListKey = reader.GetString(0);
                templateKey = reader.GetString(1);
                assignedUserId = reader.IsDBNull(2) ? null : reader.GetString(2);
            }

            var template = BoardTemplates.Get(templateKey);
            var actorRole = isEngineer ? BoardAddCardRole.Engineer : BoardAddCardRole.Customer;
            var transitionRule = template.FindTransition(currentListKey, targetListKey, actorRole);
            if (transitionRule is null)
            {
                transaction.Rollback();
                return BoardMoveResult.InvalidTransition;
            }

            // Klasik'teki Todo->Test akisinin (once uzerine al, sonra tasi) tum
            // sablonlara genellenmis hali: mühendis rolündeki bir gecis icin kart
            // once birine atanmis olmali; Admin haricinde sadece atanan mühendis tasiyabilir.
            if (transitionRule.AllowedRole == BoardAddCardRole.Engineer &&
                (assignedUserId is null || (!isAdmin && !string.Equals(assignedUserId, actingUserId, StringComparison.Ordinal))))
            {
                transaction.Rollback();
                return BoardMoveResult.RequiresAssignment;
            }

            if (transitionRule.RequiresNote && string.IsNullOrWhiteSpace(note))
            {
                transaction.Rollback();
                return BoardMoveResult.NoteRequired;
            }

            var nextOrder = await GetListCountAsync(connection, transaction, boardId, targetListKey);

            using var updateCommand = new SqlCommand(@"
                UPDATE dbo.BoardCards
                SET ListKey = @Target, SortOrder = @SortOrder
                    " + (transitionRule.IsRejection ? ", LastRejectionNote = @Note, LastRejectedAt = @Now, RejectedCount = RejectedCount + 1" : "") + @"
                WHERE Id = @Id", connection, transaction);
            updateCommand.Parameters.AddWithValue("@Id", cardId);
            updateCommand.Parameters.AddWithValue("@Target", targetListKey);
            updateCommand.Parameters.AddWithValue("@SortOrder", nextOrder);
            if (transitionRule.IsRejection)
            {
                updateCommand.Parameters.AddWithValue("@Note", note!.Trim());
                updateCommand.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            }
            await updateCommand.ExecuteNonQueryAsync();

            transaction.Commit();

            if (transitionRule.IsApproval || transitionRule.IsRejection)
            {
                await LogApprovalStatEventsAsync(cardId, boardId, assignedUserId, actingUserId, approved: transitionRule.IsApproval);
            }

            return BoardMoveResult.Success;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task SetCardApprovalStatusAsync(int cardId, string status, string actorUserId)
    {
        int boardId;
        string? assignedUserId;

        using (var connection = CreateConnection())
        {
            await connection.OpenAsync();

            using (var lookupCommand = new SqlCommand(
                "SELECT BoardId, AssignedUserId FROM dbo.BoardCards WHERE Id = @Id", connection))
            {
                lookupCommand.Parameters.AddWithValue("@Id", cardId);
                using var reader = await lookupCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return;
                }
                boardId = reader.GetInt32(0);
                assignedUserId = reader.IsDBNull(1) ? null : reader.GetString(1);
            }

            using var command = new SqlCommand(
                "UPDATE dbo.BoardCards SET ApprovalStatus = @Status WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@Id", cardId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        await LogApprovalStatEventsAsync(cardId, boardId, assignedUserId, actorUserId, approved: status == "Approved");
    }

    public async Task<bool> TryAdvanceSprintRoundAsync(int boardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            int currentRound;
            using (var command = new SqlCommand(
                "SELECT CurrentSprintRound FROM dbo.Boards WHERE Id = @BoardId", connection, transaction))
            {
                command.Parameters.AddWithValue("@BoardId", boardId);
                var result = await command.ExecuteScalarAsync();
                if (result is not int round)
                {
                    transaction.Rollback();
                    return false;
                }
                currentRound = round;
            }

            // Akis listeleri (Sprint Done haric) bu turde bos mu?
            using (var command = new SqlCommand(@"
                SELECT COUNT(*) FROM dbo.BoardCards
                WHERE BoardId = @BoardId AND IsArchived = 0 AND SprintRound = @Round
                    AND ListKey IN ('backlog', 'sprint-backlog', 'working-on-bugs', 'testing')", connection, transaction))
            {
                command.Parameters.AddWithValue("@BoardId", boardId);
                command.Parameters.AddWithValue("@Round", currentRound);
                var pendingCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (pendingCount > 0)
                {
                    transaction.Rollback();
                    return false;
                }
            }

            // Sprint Done'daki her kart onaylanmis/reddedilmis mi (ve en az bir kart var mi)?
            int sprintDoneTotal;
            int sprintDonePending;
            using (var command = new SqlCommand(@"
                SELECT COUNT(*), SUM(CASE WHEN ApprovalStatus IS NULL THEN 1 ELSE 0 END)
                FROM dbo.BoardCards
                WHERE BoardId = @BoardId AND IsArchived = 0 AND SprintRound = @Round AND ListKey = 'sprint-done'", connection, transaction))
            {
                command.Parameters.AddWithValue("@BoardId", boardId);
                command.Parameters.AddWithValue("@Round", currentRound);
                using var reader = await command.ExecuteReaderAsync();
                await reader.ReadAsync();
                sprintDoneTotal = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                sprintDonePending = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            }

            if (sprintDoneTotal == 0 || sprintDonePending > 0)
            {
                transaction.Rollback();
                return false;
            }

            using (var command = new SqlCommand(
                "UPDATE dbo.Boards SET CurrentSprintRound = CurrentSprintRound + 1 WHERE Id = @BoardId", connection, transaction))
            {
                command.Parameters.AddWithValue("@BoardId", boardId);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> AssignCardToSelfAsync(int cardId, string userId, string userDisplayName)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        // Liste kisitlamasi kasitli olarak yok: Klasik'te bu her zaman Yapilacaklar
        // listesinde cagrilir, jenerik sablonlarda ise "burada uzerine alinabilir mi"
        // kontrolu (mühendis rolündeki bir gecisin kaynak listesi mi) controller
        // tarafinda sablona gore yapilir; burada tek kural kartin kimseye atanmamis olmasidir.
        // AssignedListKey, o anki ListKey'i yakalar (bkz. ReleaseCardAssignmentAsync);
        // "üstümden bırak" sadece kart bu listedeyken mumkun olur.
        using var command = new SqlCommand(@"
            UPDATE dbo.BoardCards
            SET AssignedUserId = @UserId, AssignedAt = @Now, AssignedListKey = ListKey
            WHERE Id = @Id AND AssignedUserId IS NULL", connection);
        command.Parameters.AddWithValue("@Id", cardId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);

        var affected = await command.ExecuteNonQueryAsync();
        return affected > 0;
    }

    /// <summary>
    /// AssignCardToSelfAsync'in tersi: karti kimseye atanmamis hale getirir.
    /// Sadece kart hala uzerine alindigi listedeyken (ListKey = AssignedListKey)
    /// ve cagiran kullaniciya atanmisken calisir (controller'daki kontrolun
    /// savunma amacli ikinci kez dogrulanmasi); aksi durumda hicbir sey degismez.
    /// </summary>
    public async Task<bool> ReleaseCardAssignmentAsync(int cardId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            UPDATE dbo.BoardCards
            SET AssignedUserId = NULL, AssignedAt = NULL, AssignedListKey = NULL
            WHERE Id = @Id AND AssignedUserId = @UserId AND ListKey = AssignedListKey", connection);
        command.Parameters.AddWithValue("@Id", cardId);
        command.Parameters.AddWithValue("@UserId", userId);

        var affected = await command.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public async Task<bool> MoveCardToTestAsync(int cardId, string actingUserId, bool isAdmin)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            int boardId;
            using (var lookupCommand = new SqlCommand(
                "SELECT BoardId FROM dbo.BoardCards WHERE Id = @Id", connection, transaction))
            {
                lookupCommand.Parameters.AddWithValue("@Id", cardId);
                var result = await lookupCommand.ExecuteScalarAsync();
                if (result is null)
                {
                    transaction.Rollback();
                    return false;
                }
                boardId = Convert.ToInt32(result);
            }

            var nextOrder = await GetListCountAsync(connection, transaction, boardId, BoardLists.Test);

            var whereClause = isAdmin
                ? "Id = @Id AND ListKey = @Todo AND AssignedUserId IS NOT NULL"
                : "Id = @Id AND ListKey = @Todo AND AssignedUserId = @UserId";

            using var command = new SqlCommand($@"
                UPDATE dbo.BoardCards
                SET ListKey = @Test, MovedToTestAt = @Now, SortOrder = @SortOrder
                WHERE {whereClause}", connection, transaction);
            command.Parameters.AddWithValue("@Id", cardId);
            command.Parameters.AddWithValue("@UserId", actingUserId);
            command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("@Todo", BoardLists.Todo);
            command.Parameters.AddWithValue("@Test", BoardLists.Test);
            command.Parameters.AddWithValue("@SortOrder", nextOrder);

            var affected = await command.ExecuteNonQueryAsync();
            transaction.Commit();
            return affected > 0;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task ApproveCardAsync(int cardId, string actorUserId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        int boardId;
        string? assignedUserId;

        try
        {
            using (var lookupCommand = new SqlCommand(
                "SELECT BoardId, AssignedUserId FROM dbo.BoardCards WHERE Id = @Id", connection, transaction))
            {
                lookupCommand.Parameters.AddWithValue("@Id", cardId);
                using var reader = await lookupCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    transaction.Rollback();
                    return;
                }
                boardId = reader.GetInt32(0);
                assignedUserId = reader.IsDBNull(1) ? null : reader.GetString(1);
            }

            var nextOrder = await GetListCountAsync(connection, transaction, boardId, BoardLists.Done);

            using (var command = new SqlCommand(@"
                UPDATE dbo.BoardCards
                SET ListKey = @Done, CompletedAt = @Now, SortOrder = @SortOrder
                WHERE Id = @Id", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", cardId);
                command.Parameters.AddWithValue("@Done", BoardLists.Done);
                command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                command.Parameters.AddWithValue("@SortOrder", nextOrder);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        await LogApprovalStatEventsAsync(cardId, boardId, assignedUserId, actorUserId, approved: true);
    }

    public async Task RejectCardAsync(int cardId, string note, string actorUserId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        int boardId;
        string? assignedUserId;

        try
        {
            using (var lookupCommand = new SqlCommand(
                "SELECT BoardId, AssignedUserId FROM dbo.BoardCards WHERE Id = @Id", connection, transaction))
            {
                lookupCommand.Parameters.AddWithValue("@Id", cardId);
                using var reader = await lookupCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    transaction.Rollback();
                    return;
                }
                boardId = reader.GetInt32(0);
                assignedUserId = reader.IsDBNull(1) ? null : reader.GetString(1);
            }

            var nextOrder = await GetListCountAsync(connection, transaction, boardId, BoardLists.Todo);

            using (var command = new SqlCommand(@"
                UPDATE dbo.BoardCards
                SET ListKey = @Todo, AssignedUserId = NULL, AssignedAt = NULL,
                    LastRejectionNote = @Note, LastRejectedAt = @Now, RejectedCount = RejectedCount + 1,
                    SortOrder = @SortOrder
                WHERE Id = @Id", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", cardId);
                command.Parameters.AddWithValue("@Todo", BoardLists.Todo);
                command.Parameters.AddWithValue("@Note", note);
                command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                command.Parameters.AddWithValue("@SortOrder", nextOrder);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        await LogApprovalStatEventsAsync(cardId, boardId, assignedUserId, actorUserId, approved: false);
    }

    /// <summary>
    /// "Bana ait onay/red" (kartin sahibi icin) ve "diger onay/red" (sahip
    /// disindaki tum mühendisler icin, fan-out) istatistik olaylarini kaydeder.
    /// Aktörle sahibi ayniysa sahibe kayit atilmaz (kendine bildirim uretilmez).
    /// </summary>
    private async Task LogApprovalStatEventsAsync(int cardId, int boardId, string? assignedUserId, string actorUserId, bool approved)
    {
        var now = DateTimeOffset.UtcNow;
        var mineKey = approved ? EngineerStatKeys.CardApprovedMine : EngineerStatKeys.CardRejectedMine;
        var otherKey = approved ? EngineerStatKeys.CardApprovedOther : EngineerStatKeys.CardRejectedOther;

        if (!string.IsNullOrEmpty(assignedUserId) && !string.Equals(assignedUserId, actorUserId, StringComparison.Ordinal))
        {
            await LogStatEventAsync(assignedUserId, mineKey, "Card", cardId, boardId, now);
        }

        var others = await GetOtherEmployeeUserIdsAsync(actorUserId, assignedUserId);
        foreach (var otherUserId in others)
        {
            await LogStatEventAsync(otherUserId, otherKey, "Card", cardId, boardId, now);
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

    /// <summary>Verilen karti sahiplenen (ve kartin sahibi hariç aktörden farkli) tüm Employee kullanicilarini doner.</summary>
    private async Task<List<string>> GetOtherEmployeeUserIdsAsync(string excludeUserId1, string? excludeUserId2)
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

    public async Task<BoardCard?> GetCardByIdAsync(int cardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand($@"
            SELECT {SelectCardColumns}
            {SelectCardFrom}
            WHERE c.Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", cardId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return MapCard(reader);
    }

    public async Task DeleteCardAsync(int cardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // BoardLabels.CardId FK'si kasti olarak NO ACTION (bkz. SQL script notu:
            // Boards -> BoardLabels ve Boards -> BoardCards -> BoardLabels ayni anda
            // cascade olamaz); bu yuzden kartin etiketleri once elle silinir.
            using (var command = new SqlCommand(
                "DELETE FROM dbo.BoardLabels WHERE CardId = @Id", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", cardId);
                await command.ExecuteNonQueryAsync();
            }

            using (var command = new SqlCommand(
                "DELETE FROM dbo.BoardCards WHERE Id = @Id", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", cardId);
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

    public async Task<List<(BoardCard Card, string TemplateKey)>> GetMyAssignedTasksAsync(string userId)
    {
        var result = new List<(BoardCard, string)>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand($@"
            SELECT {SelectCardColumns}, b.TemplateKey
            {SelectCardFrom}
            WHERE c.AssignedUserId = @UserId AND c.IsArchived = 0 AND b.IsPreview = 0
            ORDER BY c.AssignedAt DESC", connection);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var card = MapCard(reader);
            var templateKey = reader.GetString(26);
            result.Add((card, templateKey));
        }

        return result;
    }

    /// <summary>
    /// Kartin UZERINDE GORUNEN etiketleri doner (yalnizca secili olanlar).
    /// NOT: Burasi sadece okuma yapar; hicbir etiket olusturmaz. Panelde
    /// listelenecek tam palet icin bkz. <see cref="GetCardLabelPaletteAsync"/>.
    /// </summary>
    public async Task<List<BoardLabel>> GetLabelsForCardAsync(int boardId, int cardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        var result = new List<BoardLabel>();
        using (var command = new SqlCommand(
            "SELECT Id, BoardId, CardId, Name, Color, IsSelected FROM dbo.BoardLabels WHERE CardId = @CardId AND IsSelected = 1 ORDER BY Id", connection))
        {
            command.Parameters.AddWithValue("@CardId", cardId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(MapLabel(reader));
            }
        }

        return result;
    }

    /// <summary>
    /// "Etiketleri Düzenle" ekraninin listesi: kartin secili/secisiz TUM etiketleri.
    /// Kartta varsayilan uc renk (isimsiz sarı/mor/mavi) yoksa bunlar SECILMEMIS
    /// olarak olusturulur; yani listede hazir dururlar ama kullanici kutucugu
    /// isaretlemeden kartta gorunmezler.
    /// </summary>
    public async Task<List<BoardLabel>> GetCardLabelPaletteAsync(int boardId, int cardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        async Task<List<BoardLabel>> ReadAllAsync()
        {
            var labels = new List<BoardLabel>();
            using var command = new SqlCommand(
                "SELECT Id, BoardId, CardId, Name, Color, IsSelected FROM dbo.BoardLabels WHERE CardId = @CardId ORDER BY Id", connection);
            command.Parameters.AddWithValue("@CardId", cardId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                labels.Add(MapLabel(reader));
            }
            return labels;
        }

        var result = await ReadAllAsync();

        // Eksik varsayilan renkleri tamamla. Eslesme SADECE RENGE bakar: kullanici
        // varsayilan bir etikete ad verdiginde o satir artik "isimsiz" olmadigi icin
        // eskiden eksik sayilip ayni renkten YENI bir etiket olusturuluyordu (duzenleme
        // yeni etiket yaratiyormus gibi gorunuyordu). Renk zaten listede varsa, adi ne
        // olursa olsun tekrar eklenmez.
        var missing = BoardLabelColors.DefaultPaletteColors
            .Where(color => !result.Any(l =>
                string.Equals(l.Color, color, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count == 0)
        {
            return result;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var color in missing)
        {
            using var insertCommand = new SqlCommand(@"
                INSERT INTO dbo.BoardLabels (BoardId, CardId, Name, Color, CreatedAt, IsSelected)
                OUTPUT INSERTED.Id
                VALUES (@BoardId, @CardId, N'', @Color, @Now, 0)", connection);
            insertCommand.Parameters.AddWithValue("@BoardId", boardId);
            insertCommand.Parameters.AddWithValue("@CardId", cardId);
            insertCommand.Parameters.AddWithValue("@Color", color);
            insertCommand.Parameters.AddWithValue("@Now", now);
            var newId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync());
            result.Add(new BoardLabel
            {
                Id = newId,
                BoardId = boardId,
                CardId = cardId,
                Name = string.Empty,
                Color = color,
                IsSelected = false
            });
        }

        return result.OrderBy(l => l.Id).ToList();
    }

    /// <summary>
    /// Bir etiketin kartta gorunup gorunmeyecegini belirler (etiketler ekranindaki
    /// kutucuk). Etiket silinmez; isareti kaldirilan etiket listede kalmaya devam
    /// eder, boylece adi/rengi kaybolmaz.
    /// </summary>
    public async Task<bool> SetLabelSelectedAsync(int labelId, int cardId, bool isSelected, string? actorUserId = null)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        int affected;
        using (var command = new SqlCommand(
            "UPDATE dbo.BoardLabels SET IsSelected = @IsSelected WHERE Id = @Id AND CardId = @CardId", connection))
        {
            command.Parameters.AddWithValue("@Id", labelId);
            command.Parameters.AddWithValue("@CardId", cardId);
            command.Parameters.AddWithValue("@IsSelected", isSelected);

            affected = await command.ExecuteNonQueryAsync();
        }

        // "Etiketler/yorumlar" istatistigi: etiketin karta EKLENMESI (kutucugun
        // isaretlenmesi) de yeni etiket olusturmak gibi sayilir. Isaretin
        // kaldirilmasi bir bildirim degildir, sayilmaz.
        if (affected > 0 && isSelected)
        {
            await LogCardLabelStatEventAsync(connection, cardId, actorUserId);
        }

        return affected > 0;
    }

    public async Task<BoardLabel?> GetLabelByIdAsync(int labelId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT Id, BoardId, CardId, Name, Color, IsSelected FROM dbo.BoardLabels WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", labelId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return MapLabel(reader);
    }

    public async Task<BoardLabel> CreateLabelForCardAsync(int boardId, int cardId, string name, string color, string? actorUserId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        var now = DateTimeOffset.UtcNow;
        int newId;
        // Yeni etiket SECILMEMIS olarak eklenir; kartta gorunmesi icin kullanici
        // etiketler ekranindan kutucugunu kendisi isaretler.
        using (var command = new SqlCommand(@"
            INSERT INTO dbo.BoardLabels (BoardId, CardId, Name, Color, CreatedAt, IsSelected)
            OUTPUT INSERTED.Id
            VALUES (@BoardId, @CardId, @Name, @Color, @Now, 0)", connection))
        {
            command.Parameters.AddWithValue("@BoardId", boardId);
            command.Parameters.AddWithValue("@CardId", cardId);
            command.Parameters.AddWithValue("@Name", name);
            command.Parameters.AddWithValue("@Color", color);
            command.Parameters.AddWithValue("@Now", now);
            newId = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        // "Etiketler/yorumlar" istatistigi: kartin sahibi disinda biri yeni etiket eklerse kaydedilir.
        await LogCardLabelStatEventAsync(connection, cardId, actorUserId);

        return new BoardLabel { Id = newId, BoardId = boardId, CardId = cardId, Name = name, Color = color };
    }

    /// <summary>
    /// "Etiketler/yorumlar" sayacinin etiket tarafi: kartin sahibi disinda biri
    /// karta etiket eklediginde (yeni olusturma, var olan etiketi isaretleme veya
    /// etiketi guncelleme) kart sahibine olay yazar.
    ///
    /// Ayni kart icin halihazirda GORULMEMIS bir etiket olayi varsa yenisi
    /// yazilmaz: kutucuk isaretleme/kaldirma hizlica tekrarlanabilen bir islem
    /// oldugu icin sayac aksi halde suniyle sisip cekmecede ayni karti defalarca
    /// listeler.
    /// </summary>
    private async Task LogCardLabelStatEventAsync(SqlConnection connection, int cardId, string? actorUserId)
    {
        if (string.IsNullOrEmpty(actorUserId))
        {
            return;
        }

        int boardId;
        string? assignedUserId;
        using (var cardLookup = new SqlCommand(
            "SELECT BoardId, AssignedUserId FROM dbo.BoardCards WHERE Id = @Id", connection))
        {
            cardLookup.Parameters.AddWithValue("@Id", cardId);
            using var reader = await cardLookup.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return;
            }

            boardId = reader.GetInt32(0);
            assignedUserId = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        if (string.IsNullOrEmpty(assignedUserId) || string.Equals(assignedUserId, actorUserId, StringComparison.Ordinal))
        {
            return;
        }

        using (var duplicateCheck = new SqlCommand(@"
            SELECT TOP 1 1 FROM dbo.EngineerStatEvents
            WHERE UserId = @UserId AND StatKey = @StatKey AND EntityType = 'Card'
              AND EntityId = @CardId AND SeenAt IS NULL", connection))
        {
            duplicateCheck.Parameters.AddWithValue("@UserId", assignedUserId);
            duplicateCheck.Parameters.AddWithValue("@StatKey", EngineerStatKeys.CardLabelAdded);
            duplicateCheck.Parameters.AddWithValue("@CardId", cardId);
            if (await duplicateCheck.ExecuteScalarAsync() is not null)
            {
                return;
            }
        }

        await LogStatEventAsync(assignedUserId, EngineerStatKeys.CardLabelAdded, "Card", cardId, boardId, DateTimeOffset.UtcNow);
    }

    /// <summary>Sadece verilen kartın kendi etiketi ise günceller (başka bir karta ait etikete dokunmaz).</summary>
    public async Task<bool> UpdateLabelAsync(int labelId, int cardId, string name, string color, string? actorUserId = null)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        int affected;
        using (var command = new SqlCommand(
            "UPDATE dbo.BoardLabels SET Name = @Name, Color = @Color WHERE Id = @Id AND CardId = @CardId", connection))
        {
            command.Parameters.AddWithValue("@Id", labelId);
            command.Parameters.AddWithValue("@CardId", cardId);
            command.Parameters.AddWithValue("@Name", name);
            command.Parameters.AddWithValue("@Color", color);
            affected = await command.ExecuteNonQueryAsync();
        }

        // "Etiketler/yorumlar" istatistigi: var olan bir etiketin adinin/renginin
        // degistirilmesi de kartin sahibi icin bir degisikliktir, sayilir.
        if (affected > 0)
        {
            await LogCardLabelStatEventAsync(connection, cardId, actorUserId);
        }

        return affected > 0;
    }

    /// <summary>Sadece verilen kartın kendi etiketi ise siler (başka bir karta ait etikete dokunmaz).</summary>
    public async Task<bool> DeleteLabelAsync(int labelId, int cardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "DELETE FROM dbo.BoardLabels WHERE Id = @Id AND CardId = @CardId", connection);
        command.Parameters.AddWithValue("@Id", labelId);
        command.Parameters.AddWithValue("@CardId", cardId);
        var affected = await command.ExecuteNonQueryAsync();
        return affected > 0;
    }

    private static BoardLabel MapLabel(SqlDataReader reader, int offset = 0)
    {
        return new BoardLabel
        {
            Id = reader.GetInt32(offset),
            BoardId = reader.GetInt32(offset + 1),
            CardId = reader.IsDBNull(offset + 2) ? 0 : reader.GetInt32(offset + 2),
            Name = reader.GetString(offset + 3),
            Color = reader.GetString(offset + 4),
            IsSelected = !reader.IsDBNull(offset + 5) && reader.GetBoolean(offset + 5)
        };
    }

    private static Board MapBoard(SqlDataReader reader)
    {
        return new Board
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            CreatedByUserId = reader.GetString(2),
            CreatedByDisplayName = reader.GetString(3),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
            TodoColor = reader.IsDBNull(5) ? null : reader.GetString(5),
            TestColor = reader.IsDBNull(6) ? null : reader.GetString(6),
            DoneColor = reader.IsDBNull(7) ? null : reader.GetString(7),
            TemplateKey = reader.GetString(8),
            CurrentSprintRound = reader.GetInt32(9),
            IsPreview = reader.GetBoolean(reader.GetOrdinal("IsPreview"))
        };
    }

    private static BoardCard MapCard(SqlDataReader reader)
    {
        return new BoardCard
        {
            Id = reader.GetInt32(0),
            BoardId = reader.GetInt32(1),
            ListKey = reader.GetString(2),
            Title = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedByUserId = reader.GetString(5),
            CreatedByDisplayName = reader.GetString(6),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(7),
            AssignedUserId = reader.IsDBNull(8) ? null : reader.GetString(8),
            AssignedUserDisplayName = reader.IsDBNull(9) ? null : reader.GetString(9),
            AssignedAt = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            MovedToTestAt = reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            CompletedAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            LastRejectionNote = reader.IsDBNull(13) ? null : reader.GetString(13),
            LastRejectedAt = reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            RejectedCount = reader.GetInt32(15),
            BoardTitle = reader.GetString(16),
            CoverColor = reader.IsDBNull(17) ? null : reader.GetString(17),
            CoverImagePath = reader.IsDBNull(18) ? null : reader.GetString(18),
            SortOrder = reader.GetInt32(19),
            IsArchived = reader.GetBoolean(20),
            ArchivedAt = reader.IsDBNull(21) ? null : reader.GetFieldValue<DateTimeOffset>(21),
            ArchivedByUserId = reader.IsDBNull(22) ? null : reader.GetString(22),
            SprintRound = reader.GetInt32(23),
            ApprovalStatus = reader.IsDBNull(24) ? null : reader.GetString(24),
            AssignedListKey = reader.IsDBNull(25) ? null : reader.GetString(25)
        };
    }

    public async Task SetCardCoverColorAsync(int cardId, string color)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.BoardCards SET CoverColor = @Color, CoverImagePath = NULL WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", cardId);
        command.Parameters.AddWithValue("@Color", color);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SetCardCoverImageAsync(int cardId, string imagePath)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.BoardCards SET CoverImagePath = @Path, CoverColor = NULL WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", cardId);
        command.Parameters.AddWithValue("@Path", imagePath);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ClearCardCoverAsync(int cardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.BoardCards SET CoverColor = NULL, CoverImagePath = NULL WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", cardId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<string, int>> GetCardCountByListAsync(int boardId)
    {
        var result = new Dictionary<string, int>
        {
            [BoardLists.Todo] = 0,
            [BoardLists.Test] = 0,
            [BoardLists.Done] = 0
        };

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT ListKey, COUNT(*) FROM dbo.BoardCards WHERE BoardId = @BoardId AND IsArchived = 0 GROUP BY ListKey", connection);
        command.Parameters.AddWithValue("@BoardId", boardId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
        }

        return result;
    }

    public async Task MoveCardAsync(int cardId, int boardId, string targetListKey, int targetPosition)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            string currentListKey;
            int currentOrder;
            using (var command = new SqlCommand(
                "SELECT ListKey, SortOrder FROM dbo.BoardCards WHERE Id = @Id AND BoardId = @BoardId", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", cardId);
                command.Parameters.AddWithValue("@BoardId", boardId);
                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    transaction.Rollback();
                    return;
                }
                currentListKey = reader.GetString(0);
                currentOrder = reader.GetInt32(1);
            }

            var targetIndex = Math.Max(0, targetPosition - 1);

            if (string.Equals(currentListKey, targetListKey, StringComparison.Ordinal))
            {
                var count = await GetListCountAsync(connection, transaction, boardId, currentListKey);
                targetIndex = Math.Min(targetIndex, count - 1);

                if (targetIndex == currentOrder)
                {
                    transaction.Commit();
                    return;
                }

                if (targetIndex < currentOrder)
                {
                    using var shiftCommand = new SqlCommand(@"
                        UPDATE dbo.BoardCards SET SortOrder = SortOrder + 1
                        WHERE BoardId = @BoardId AND ListKey = @ListKey AND IsArchived = 0 AND SortOrder >= @NewIndex AND SortOrder < @OldIndex",
                        connection, transaction);
                    shiftCommand.Parameters.AddWithValue("@BoardId", boardId);
                    shiftCommand.Parameters.AddWithValue("@ListKey", currentListKey);
                    shiftCommand.Parameters.AddWithValue("@NewIndex", targetIndex);
                    shiftCommand.Parameters.AddWithValue("@OldIndex", currentOrder);
                    await shiftCommand.ExecuteNonQueryAsync();
                }
                else
                {
                    using var shiftCommand = new SqlCommand(@"
                        UPDATE dbo.BoardCards SET SortOrder = SortOrder - 1
                        WHERE BoardId = @BoardId AND ListKey = @ListKey AND IsArchived = 0 AND SortOrder > @OldIndex AND SortOrder <= @NewIndex",
                        connection, transaction);
                    shiftCommand.Parameters.AddWithValue("@BoardId", boardId);
                    shiftCommand.Parameters.AddWithValue("@ListKey", currentListKey);
                    shiftCommand.Parameters.AddWithValue("@OldIndex", currentOrder);
                    shiftCommand.Parameters.AddWithValue("@NewIndex", targetIndex);
                    await shiftCommand.ExecuteNonQueryAsync();
                }

                using (var updateCommand = new SqlCommand(
                    "UPDATE dbo.BoardCards SET SortOrder = @NewIndex WHERE Id = @Id", connection, transaction))
                {
                    updateCommand.Parameters.AddWithValue("@Id", cardId);
                    updateCommand.Parameters.AddWithValue("@NewIndex", targetIndex);
                    await updateCommand.ExecuteNonQueryAsync();
                }
            }
            else
            {
                using (var closeGapCommand = new SqlCommand(@"
                    UPDATE dbo.BoardCards SET SortOrder = SortOrder - 1
                    WHERE BoardId = @BoardId AND ListKey = @ListKey AND IsArchived = 0 AND SortOrder > @OldIndex",
                    connection, transaction))
                {
                    closeGapCommand.Parameters.AddWithValue("@BoardId", boardId);
                    closeGapCommand.Parameters.AddWithValue("@ListKey", currentListKey);
                    closeGapCommand.Parameters.AddWithValue("@OldIndex", currentOrder);
                    await closeGapCommand.ExecuteNonQueryAsync();
                }

                var targetCount = await GetListCountAsync(connection, transaction, boardId, targetListKey);
                targetIndex = Math.Min(targetIndex, targetCount);

                using (var makeRoomCommand = new SqlCommand(@"
                    UPDATE dbo.BoardCards SET SortOrder = SortOrder + 1
                    WHERE BoardId = @BoardId AND ListKey = @ListKey AND IsArchived = 0 AND SortOrder >= @NewIndex",
                    connection, transaction))
                {
                    makeRoomCommand.Parameters.AddWithValue("@BoardId", boardId);
                    makeRoomCommand.Parameters.AddWithValue("@ListKey", targetListKey);
                    makeRoomCommand.Parameters.AddWithValue("@NewIndex", targetIndex);
                    await makeRoomCommand.ExecuteNonQueryAsync();
                }

                using (var updateCommand = new SqlCommand(
                    "UPDATE dbo.BoardCards SET ListKey = @ListKey, SortOrder = @NewIndex WHERE Id = @Id", connection, transaction))
                {
                    updateCommand.Parameters.AddWithValue("@Id", cardId);
                    updateCommand.Parameters.AddWithValue("@ListKey", targetListKey);
                    updateCommand.Parameters.AddWithValue("@NewIndex", targetIndex);
                    await updateCommand.ExecuteNonQueryAsync();
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<int> GetListCountAsync(SqlConnection connection, SqlTransaction transaction, int boardId, string listKey)
    {
        using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.BoardCards WHERE BoardId = @BoardId AND ListKey = @ListKey AND IsArchived = 0", connection, transaction);
        command.Parameters.AddWithValue("@BoardId", boardId);
        command.Parameters.AddWithValue("@ListKey", listKey);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task ArchiveCardAsync(int cardId, string userId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            UPDATE dbo.BoardCards
            SET IsArchived = 1, ArchivedAt = @Now, ArchivedByUserId = @UserId
            WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", cardId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    public async Task RestoreCardAsync(int cardId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            int boardId;
            string listKey;
            using (var lookupCommand = new SqlCommand(
                "SELECT BoardId, ListKey FROM dbo.BoardCards WHERE Id = @Id", connection, transaction))
            {
                lookupCommand.Parameters.AddWithValue("@Id", cardId);
                using var reader = await lookupCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    transaction.Rollback();
                    return;
                }
                boardId = reader.GetInt32(0);
                listKey = reader.GetString(1);
            }

            var nextOrder = await GetListCountAsync(connection, transaction, boardId, listKey);

            using (var command = new SqlCommand(@"
                UPDATE dbo.BoardCards
                SET IsArchived = 0, ArchivedAt = NULL, ArchivedByUserId = NULL, SortOrder = @SortOrder
                WHERE Id = @Id", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", cardId);
                command.Parameters.AddWithValue("@SortOrder", nextOrder);
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

    public async Task<List<BoardCard>> GetArchivedCardsForUserAsync(string userId)
    {
        var result = new List<BoardCard>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand($@"
            SELECT {SelectCardColumns}
            {SelectCardFrom}
            WHERE c.IsArchived = 1 AND c.ArchivedByUserId = @UserId AND b.IsPreview = 0
            ORDER BY c.ArchivedAt DESC", connection);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapCard(reader));
        }

        return result;
    }

    public async Task UpdateCardDescriptionAsync(int cardId, string? descriptionHtml)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.BoardCards SET Description = @Description WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", cardId);
        command.Parameters.AddWithValue("@Description", (object?)descriptionHtml ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<BoardCardAttachment> AddCardAttachmentLinkAsync(int cardId, string url, string userId, string displayName)
    {
        return await InsertAttachmentAsync(cardId, "link", url, null, userId, displayName);
    }

    public async Task<BoardCardAttachment> AddCardAttachmentFileAsync(int cardId, string filePath, string fileName, string userId, string displayName)
    {
        return await InsertAttachmentAsync(cardId, "file", filePath, fileName, userId, displayName);
    }

    private async Task<BoardCardAttachment> InsertAttachmentAsync(int cardId, string type, string url, string? fileName, string userId, string displayName)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        var now = DateTimeOffset.UtcNow;
        using var command = new SqlCommand(@"
            INSERT INTO dbo.BoardCardAttachments (CardId, AttachmentType, Url, FileName, CreatedByUserId, CreatedByDisplayName, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@CardId, @Type, @Url, @FileName, @UserId, @DisplayName, @Now)", connection);
        command.Parameters.AddWithValue("@CardId", cardId);
        command.Parameters.AddWithValue("@Type", type);
        command.Parameters.AddWithValue("@Url", url);
        command.Parameters.AddWithValue("@FileName", (object?)fileName ?? DBNull.Value);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@DisplayName", displayName);
        command.Parameters.AddWithValue("@Now", now);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());

        return new BoardCardAttachment
        {
            Id = id,
            CardId = cardId,
            AttachmentType = type,
            Url = url,
            FileName = fileName,
            CreatedByUserId = userId,
            CreatedByDisplayName = displayName,
            CreatedAt = now
        };
    }

    public async Task<List<BoardCardAttachment>> GetAttachmentsForCardAsync(int cardId)
    {
        var result = new List<BoardCardAttachment>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT Id, CardId, AttachmentType, Url, FileName, CreatedByUserId, CreatedByDisplayName, CreatedAt
            FROM dbo.BoardCardAttachments
            WHERE CardId = @CardId
            ORDER BY CreatedAt DESC", connection);
        command.Parameters.AddWithValue("@CardId", cardId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapAttachment(reader));
        }

        return result;
    }

    public async Task<BoardCardAttachment?> GetAttachmentByIdAsync(int attachmentId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT Id, CardId, AttachmentType, Url, FileName, CreatedByUserId, CreatedByDisplayName, CreatedAt
            FROM dbo.BoardCardAttachments
            WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", attachmentId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return MapAttachment(reader);
    }

    public async Task DeleteCardAttachmentAsync(int attachmentId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "DELETE FROM dbo.BoardCardAttachments WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", attachmentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<BoardCardComment> AddCardCommentAsync(int cardId, string userId, string displayName, string role, string bodyHtml)
    {
        int boardId;
        string? assignedUserId;

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using (var cardLookup = new SqlCommand(
            "SELECT BoardId, AssignedUserId FROM dbo.BoardCards WHERE Id = @Id", connection))
        {
            cardLookup.Parameters.AddWithValue("@Id", cardId);
            using var cardReader = await cardLookup.ExecuteReaderAsync();
            await cardReader.ReadAsync();
            boardId = cardReader.GetInt32(0);
            assignedUserId = cardReader.IsDBNull(1) ? null : cardReader.GetString(1);
        }

        var now = DateTimeOffset.UtcNow;
        using var command = new SqlCommand(@"
            INSERT INTO dbo.BoardCardComments (CardId, UserId, DisplayName, Role, Body, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@CardId, @UserId, @DisplayName, @Role, @Body, @Now)", connection);
        command.Parameters.AddWithValue("@CardId", cardId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@DisplayName", displayName);
        command.Parameters.AddWithValue("@Role", role);
        command.Parameters.AddWithValue("@Body", bodyHtml);
        command.Parameters.AddWithValue("@Now", now);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());

        // "Etiketler/yorumlar" istatistigi: sadece kartin sahibi disinda biri (ve
        // otomatik "system" yorumu degil) yorum eklerse kaydedilir.
        if (!string.IsNullOrEmpty(assignedUserId) && !string.Equals(assignedUserId, userId, StringComparison.Ordinal)
            && !string.Equals(userId, "system", StringComparison.Ordinal))
        {
            await LogStatEventAsync(assignedUserId, EngineerStatKeys.CardCommentAdded, "Card", cardId, boardId, now);
        }

        return new BoardCardComment
        {
            Id = id,
            CardId = cardId,
            UserId = userId,
            DisplayName = displayName,
            Role = role,
            Body = bodyHtml,
            CreatedAt = now
        };
    }

    public async Task<List<BoardCardComment>> GetCommentsForCardAsync(int cardId)
    {
        var result = new List<BoardCardComment>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT Id, CardId, UserId, DisplayName, Role, Body, CreatedAt
            FROM dbo.BoardCardComments
            WHERE CardId = @CardId
            ORDER BY CreatedAt DESC", connection);
        command.Parameters.AddWithValue("@CardId", cardId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new BoardCardComment
            {
                Id = reader.GetInt32(0),
                CardId = reader.GetInt32(1),
                UserId = reader.GetString(2),
                DisplayName = reader.GetString(3),
                Role = reader.GetString(4),
                Body = reader.GetString(5),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(6)
            });
        }

        return result;
    }

    public async Task<BoardCardComment?> GetCommentByIdAsync(int commentId)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT Id, CardId, UserId, DisplayName, Role, Body, CreatedAt
            FROM dbo.BoardCardComments
            WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", commentId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new BoardCardComment
        {
            Id = reader.GetInt32(0),
            CardId = reader.GetInt32(1),
            UserId = reader.GetString(2),
            DisplayName = reader.GetString(3),
            Role = reader.GetString(4),
            Body = reader.GetString(5),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(6)
        };
    }

    public async Task UpdateCardCommentAsync(int commentId, string bodyHtml)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "UPDATE dbo.BoardCardComments SET Body = @Body WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", commentId);
        command.Parameters.AddWithValue("@Body", bodyHtml);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ToggleCommentReactionAsync(int commentId, string userId, string displayName, string emoji)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            int? existingId = null;
            string? existingEmoji = null;
            using (var checkCommand = new SqlCommand(
                "SELECT Id, Emoji FROM dbo.BoardCardCommentReactions WHERE CommentId = @CommentId AND UserId = @UserId", connection, transaction))
            {
                checkCommand.Parameters.AddWithValue("@CommentId", commentId);
                checkCommand.Parameters.AddWithValue("@UserId", userId);
                using var reader = await checkCommand.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    existingId = reader.GetInt32(0);
                    existingEmoji = reader.GetString(1);
                }
            }

            if (existingId.HasValue && string.Equals(existingEmoji, emoji, StringComparison.Ordinal))
            {
                using var deleteCommand = new SqlCommand(
                    "DELETE FROM dbo.BoardCardCommentReactions WHERE Id = @Id", connection, transaction);
                deleteCommand.Parameters.AddWithValue("@Id", existingId.Value);
                await deleteCommand.ExecuteNonQueryAsync();
            }
            else if (existingId.HasValue)
            {
                using var updateCommand = new SqlCommand(
                    "UPDATE dbo.BoardCardCommentReactions SET Emoji = @Emoji, CreatedAt = @Now WHERE Id = @Id", connection, transaction);
                updateCommand.Parameters.AddWithValue("@Id", existingId.Value);
                updateCommand.Parameters.AddWithValue("@Emoji", emoji);
                updateCommand.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
                await updateCommand.ExecuteNonQueryAsync();
            }
            else
            {
                using var insertCommand = new SqlCommand(@"
                    INSERT INTO dbo.BoardCardCommentReactions (CommentId, UserId, DisplayName, Emoji, CreatedAt)
                    VALUES (@CommentId, @UserId, @DisplayName, @Emoji, @Now)", connection, transaction);
                insertCommand.Parameters.AddWithValue("@CommentId", commentId);
                insertCommand.Parameters.AddWithValue("@UserId", userId);
                insertCommand.Parameters.AddWithValue("@DisplayName", displayName);
                insertCommand.Parameters.AddWithValue("@Emoji", emoji);
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

    public async Task<List<BoardCardCommentReaction>> GetReactionsForCommentAsync(int commentId)
    {
        var result = new List<BoardCardCommentReaction>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(
            "SELECT Id, CommentId, UserId, DisplayName, Emoji, CreatedAt FROM dbo.BoardCardCommentReactions WHERE CommentId = @CommentId", connection);
        command.Parameters.AddWithValue("@CommentId", commentId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapReaction(reader));
        }

        return result;
    }

    public async Task<List<BoardCardCommentReaction>> GetReactionsForCardAsync(int cardId)
    {
        var result = new List<BoardCardCommentReaction>();

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(@"
            SELECT r.Id, r.CommentId, r.UserId, r.DisplayName, r.Emoji, r.CreatedAt
            FROM dbo.BoardCardCommentReactions r
            INNER JOIN dbo.BoardCardComments c ON c.Id = r.CommentId
            WHERE c.CardId = @CardId", connection);
        command.Parameters.AddWithValue("@CardId", cardId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapReaction(reader));
        }

        return result;
    }

    private static BoardCardCommentReaction MapReaction(SqlDataReader reader)
    {
        return new BoardCardCommentReaction
        {
            Id = reader.GetInt32(0),
            CommentId = reader.GetInt32(1),
            UserId = reader.GetString(2),
            DisplayName = reader.GetString(3),
            Emoji = reader.GetString(4),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(5)
        };
    }

    private static BoardCardAttachment MapAttachment(SqlDataReader reader)
    {
        return new BoardCardAttachment
        {
            Id = reader.GetInt32(0),
            CardId = reader.GetInt32(1),
            AttachmentType = reader.GetString(2),
            Url = reader.GetString(3),
            FileName = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedByUserId = reader.GetString(5),
            CreatedByDisplayName = reader.GetString(6),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(7)
        };
    }
}
