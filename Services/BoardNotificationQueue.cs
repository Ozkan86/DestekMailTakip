using System.Threading.Channels;

namespace task_list.Services;

public class BoardNotificationQueue : IBoardNotificationQueue
{
    private readonly Channel<BoardNotificationJob> _channel;
    private readonly ILogger<BoardNotificationQueue> _logger;

    public BoardNotificationQueue(ILogger<BoardNotificationQueue> logger)
    {
        _logger = logger;

        // Sinirli kapasite: bildirim gonderimi SMTP hizinda ilerledigi icin ani yuklerde
        // kuyruk buyuyebilir; sinirsiz buyuyup bellegi tuketmesindense en eskiyi dusururuz.
        _channel = Channel.CreateBounded<BoardNotificationJob>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    public void Enqueue(BoardNotificationJob job)
    {
        if (!_channel.Writer.TryWrite(job))
        {
            _logger.LogWarning("Pano bildirim kuyruğu dolu; bildirim işi kuyruğa alınamadı.");
        }
    }

    public IAsyncEnumerable<BoardNotificationJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
