namespace task_list.Services;

public interface IImapMailService
{
    /// <summary>
    /// Posta kutusunu senkronize eder ve ice aktarilan yeni oge (mail + yanit)
    /// sayisini doner. Dogrudan cagrilmak yerine IMailSyncCoordinator uzerinden
    /// kullanilmalidir (tek seferde tek senkronizasyon + debounce).
    /// </summary>
    Task<int> SyncAsync(CancellationToken cancellationToken);
}
