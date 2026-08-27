namespace task_list.Services;

/// <summary>
/// Pano bildirimlerinin istek yolundan ayrilmasini saglayan kuyruk.
/// <see cref="Enqueue"/> beklemeden doner; isi <see cref="BoardNotificationBackgroundService"/> tuketir.
/// </summary>
public interface IBoardNotificationQueue
{
    /// <summary>Isi kuyruga birakir. Kuyruk doluysa en eski is dusurulur (bildirim kritik veri degildir).</summary>
    void Enqueue(BoardNotificationJob job);

    IAsyncEnumerable<BoardNotificationJob> ReadAllAsync(CancellationToken cancellationToken);
}
