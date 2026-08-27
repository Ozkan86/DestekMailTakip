using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using task_list.Data;
using task_list.Models;
using task_list.Services;

namespace task_list.Controllers;

[Authorize(Roles = "Admin,Employee")]
public class MailController : Controller
{
    private readonly IMailRepository _mailRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMailSenderService _mailSenderService;
    private readonly IMailSyncCoordinator _syncCoordinator;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<MailController> _logger;

    public MailController(
        IMailRepository mailRepository,
        IMessageRepository messageRepository,
        UserManager<ApplicationUser> userManager,
        IMailSenderService mailSenderService,
        IMailSyncCoordinator syncCoordinator,
        IWebHostEnvironment environment,
        ILogger<MailController> logger)
    {
        _mailRepository = mailRepository;
        _messageRepository = messageRepository;
        _userManager = userManager;
        _mailSenderService = mailSenderService;
        _syncCoordinator = syncCoordinator;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Liste panelinin "yeni mail geldi mi?" yoklamasi icin ucuz uc: sadece
    /// senkronizasyon surumunu doner (DB/IMAP islemi yapmaz).
    /// </summary>
    [HttpGet]
    public IActionResult SyncStatus() => Json(new { version = _syncCoordinator.Version });

    /// <summary>
    /// Navbar'daki manuel senkronizasyon butonu. Periyodik senkronizasyonun
    /// sirasini beklemeden hemen bir tur calistirir ve sonucu doner; buton
    /// kullaniciya "kac yeni mail geldi" bilgisini bu yanittan gosterir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncNow()
    {
        var outcome = await _syncCoordinator.SyncManuallyAsync();

        return Json(new
        {
            ran = outcome.Ran,
            imported = outcome.Imported,
            failed = outcome.Failed,
            version = _syncCoordinator.Version
        });
    }

    /// <summary>
    /// Klasor gorunumu. <paramref name="embedded"/> yalnizca yerlesimi degistirir
    /// (ust cubuk/klasor menusu olmadan _FrameLayout); "İstatistiklerim"
    /// sayfasindaki gomulu bolumler bunu kullanir, islevler aynen calisir.
    /// </summary>
    public async Task<IActionResult> Index(string? folder, int? selected, bool onlyMine = false, bool embedded = false)
    {
        // Senkronizasyon ARTIK BEKLENMIYOR: eskiden her klasor tiklamasi/yenilemesi
        // tam bir IMAP oturumunu (connect + TLS + auth + tum UID'leri tarama) bekliyor
        // ve sayfa saniyelerce acilmiyordu. Simdi sadece tetikleniyor; yeni mail
        // geldiginde liste paneli SyncStatus'u yoklayarak kendini tazeliyor.
        // AJAX (filtre/panel) isteklerinde tetiklemeye de gerek yok.
        if (!IsAjaxRequest())
        {
            _syncCoordinator.RequestSync();
        }

        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var customFolderId = MailFolders.ParseCustomFolderId(folder);

        string currentFolder;
        List<MailMessageModel> mails;
        string? folderLabel = null;

        if (customFolderId is not null)
        {
            var customFolder = await _mailRepository.GetUserFolderAsync(customFolderId.Value, userId);
            if (customFolder is null)
            {
                // Klasor silinmis ya da bu kullaniciya ait degil; guvenli varsayilana don.
                return RedirectToAction(nameof(Index), new { folder = MailFolders.Unassigned });
            }

            currentFolder = folder!;
            folderLabel = customFolder.Name;
            mails = await _mailRepository.GetByCustomFolderAsync(customFolderId.Value, userId);
        }
        else
        {
            currentFolder = MailFolders.IsValid(folder) ? folder! : MailFolders.Unassigned;
            var canFilterOnlyMine = currentFolder == MailFolders.Closed || currentFolder == MailFolders.Sent;
            onlyMine = canFilterOnlyMine && onlyMine;
            mails = await _mailRepository.GetByFolderAsync(currentFolder, userId, onlyMine);
        }

        var selectedId = selected;
        if (selectedId is not null && !mails.Any(m => m.Id == selectedId))
        {
            selectedId = null;
        }

        // Taslaklar disindaki tum klasorler artik sayfa ilk acildiginda tam
        // genislikte listeyi gostermeli (bkz. Index.cshtml usesTaskRowDesign);
        // bu yuzden ilk maili sadece Taslaklar'da otomatik seciyoruz.
        if (selectedId is null && currentFolder == MailFolders.Drafts)
        {
            selectedId = mails.FirstOrDefault()?.Id;
        }

        var draftTemplates = currentFolder == MailFolders.Drafts
            ? await _mailRepository.GetDraftTemplatesAsync(userId)
            : new List<MailDraftTemplate>();

        var viewModel = new MailIndexViewModel
        {
            Mails = mails,
            SelectedId = selectedId,
            Folder = currentFolder,
            FolderLabel = folderLabel,
            DraftTemplates = draftTemplates,
            OnlyMine = customFolderId is null && onlyMine,
            Embedded = embedded
        };

        // "Sadece Benim" (Kapatılmış/Gönderilenler) gibi filtre degisiklikleri
        // artik tam sayfa yenilemek yerine sadece mail listesi panelini AJAX ile
        // getirip degistiriyor (bkz. Index.cshtml, _MailListPane.cshtml).
        if (IsAjaxRequest())
        {
            return PartialView("_MailListPane", viewModel);
        }

        return View(viewModel);
    }

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> Details(int id, string? folder)
    {
        var mail = await _mailRepository.GetByIdAsync(id);
        if (mail is null)
        {
            return NotFound();
        }

        if (!mail.IsRead)
        {
            await _mailRepository.MarkAsReadAsync(id);
        }

        var userId = _userManager.GetUserId(User) ?? string.Empty;
        mail.IsArchived = await _mailRepository.IsArchivedAsync(id, userId);
        mail.FolderIds = await _mailRepository.GetMailFolderIdsAsync(id, userId);
        // "Görevlerime Ait Mesajlar" istatistigi icin: bu maili en son ne zaman
        // gordugumuzu isaretler (yalnizca daha once yanit vermissek anlamli olur).
        await _mailRepository.UpsertMailReplySeenAsync(id, userId);
        ViewData["Folder"] = MailFolders.IsValidOrCustom(folder) ? folder! : MailFolders.Unassigned;
        ViewData["DraftTemplates"] = await _mailRepository.GetDraftTemplatesAsync(userId);
        ViewData["UserFolders"] = await _mailRepository.GetUserFoldersAsync(userId);
        var (previousId, nextId) = await GetAdjacentMailIdsAsync(id, folder);
        ViewData["PreviousMailId"] = previousId;
        ViewData["NextMailId"] = nextId;
        return View(mail);
    }

    // Mail listesinin sağ tarafındaki önizleme penceresi (iframe) için,
    // ana navigasyon/menü olmadan sadece mail içeriğini basan hafif sayfa.
    public async Task<IActionResult> DetailFrame(int id, string? folder, bool refresh = false)
    {
        var mail = await _mailRepository.GetByIdAsync(id);
        if (mail is null)
        {
            return NotFound();
        }

        if (!mail.IsRead)
        {
            await _mailRepository.MarkAsReadAsync(id);
        }

        var userId = _userManager.GetUserId(User) ?? string.Empty;
        mail.IsArchived = await _mailRepository.IsArchivedAsync(id, userId);
        mail.FolderIds = await _mailRepository.GetMailFolderIdsAsync(id, userId);
        // "Görevlerime Ait Mesajlar" istatistigi icin: bu maili en son ne zaman
        // gordugumuzu isaretler (yalnizca daha once yanit vermissek anlamli olur).
        await _mailRepository.UpsertMailReplySeenAsync(id, userId);
        ViewData["RefreshParent"] = refresh;
        ViewData["Folder"] = MailFolders.IsValidOrCustom(folder) ? folder! : MailFolders.Unassigned;
        ViewData["DraftTemplates"] = await _mailRepository.GetDraftTemplatesAsync(userId);
        ViewData["UserFolders"] = await _mailRepository.GetUserFoldersAsync(userId);
        var (previousId, nextId) = await GetAdjacentMailIdsAsync(id, folder);
        ViewData["PreviousMailId"] = previousId;
        ViewData["NextMailId"] = nextId;
        return View(mail);
    }

    // Bir mailin, bulundugu klasordeki (listede goruntulenme sirasina gore) bir onceki
    // ve bir sonraki mail Id'sini doner; detay sayfasindaki gezinme oklari icin.
    private async Task<(int? Previous, int? Next)> GetAdjacentMailIdsAsync(int currentId, string? folder)
    {
        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var customFolderId = MailFolders.ParseCustomFolderId(folder);

        List<int> ids;
        if (customFolderId is not null)
        {
            ids = await _mailRepository.GetCustomFolderOrderedMailIdsAsync(customFolderId.Value, userId);
        }
        else
        {
            var currentFolder = MailFolders.IsValid(folder) ? folder! : MailFolders.Unassigned;
            if (currentFolder == MailFolders.Assigned)
            {
                // Atanmis klasorunde mailler ekip uyesine gore gruplanip listelendigi
                // (bkz. _AssignedTaskGroups.cshtml: once atanan kisiye gore, sonra o
                // grup icinde tarihe gore siralanir) icin onceki/sonraki gezinme de
                // ayni gruplanmis gorunum sirasini takip etmeli; aksi halde duz tarih
                // sirasi ekrandaki siradan farkli olur ve oklar sanki farkli kisilerin
                // ayri listeleri varmis gibi ziplar.
                ids = await GetAssignedFolderVisualOrderAsync(userId);
            }
            else
            {
                ids = await _mailRepository.GetOrderedMailIdsAsync(currentFolder, userId);
            }
        }

        var index = ids.IndexOf(currentId);
        if (index < 0)
        {
            return (null, null);
        }

        int? previous = index > 0 ? ids[index - 1] : null;
        int? next = index < ids.Count - 1 ? ids[index + 1] : null;
        return (previous, next);
    }

    // _AssignedTaskGroups.cshtml ile birebir ayni gruplama/siralama: once atanan
    // kisiye (UserId, gorunen adi) gore, sonra o grup icinde tarihe gore. Bir mail
    // birden fazla kisiye atanmissa birden fazla grupta gorunur; gezinme oklari
    // icin ilk gorundugu konum kullanilir (Distinct ilk eslesmeyi korur).
    private async Task<List<int>> GetAssignedFolderVisualOrderAsync(string userId)
    {
        var mails = await _mailRepository.GetByFolderAsync(MailFolders.Assigned, userId);

        return mails
            .SelectMany(mail => mail.AssignedUsers, (mail, assignee) => new { Mail = mail, Assignee = assignee })
            .GroupBy(x => x.Assignee.UserId)
            .Select(g => new
            {
                DisplayName = g.First().Assignee.DisplayName,
                Tasks = g.Select(x => x.Mail).OrderByDescending(m => m.ReceivedAt).ToList()
            })
            .OrderBy(g => g.DisplayName)
            .SelectMany(g => g.Tasks)
            .Select(m => m.Id)
            .Distinct()
            .ToList();
    }

    // Mail gövdesi dışarıdan gelen güvensiz HTML içerebilir (script vb.).
    // Bu yüzden ana sayfaya Html.Raw ile gömülmez; sandboxed bir iframe içinde,
    // script çalıştırmayan ve ana sayfa origin'inden izole ayrı bir response olarak sunulur.
    public async Task<IActionResult> BodyFrame(int id)
    {
        var mail = await _mailRepository.GetByIdAsync(id);
        if (mail is null)
        {
            return NotFound();
        }

        var html = !string.IsNullOrEmpty(mail.BodyHtml)
            ? mail.BodyHtml
            : $"<pre style=\"white-space:pre-wrap;font-family:inherit;\">{System.Net.WebUtility.HtmlEncode(mail.BodyText)}</pre>";

        return Content(html, "text/html; charset=utf-8");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFlag(int id, string flagType, string? returnTo, string? folder)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null || !MailFlagTypes.IsValid(flagType))
        {
            return Forbid();
        }

        var requester = await _userManager.GetUserAsync(User);
        var displayName = requester?.DisplayName ?? string.Empty;
        var result = await _mailRepository.SetFlagAsync(id, flagType, userId, displayName);

        // NotAllowed durumunda arayuz zaten secenegi inaktif gosteriyor; buraya ancak
        // dogrudan POST atilirsa veya araya baska bir kullanicinin atama degisikligi
        // girerse dusulur, o yuzden kuralin kendisini metin olarak geri bildiriyoruz.
        TempData["FlagError"] = result switch
        {
            MailFlagUpdateResult.LockedByOtherUser => MailFlagPolicy.OwnerLockMessage,
            MailFlagUpdateResult.NotAllowedUnassigned => MailFlagPolicy.UnassignedClosedMessage,
            MailFlagUpdateResult.NotAllowedNotAssignee => MailFlagPolicy.NotAssigneeMessage,
            _ => null
        };

        return RedirectAfterAction(id, returnTo, folder, refresh: result == MailFlagUpdateResult.Success);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleArchive(int id, string? returnTo, string? folder)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var isArchived = await _mailRepository.IsArchivedAsync(id, userId);
        await _mailRepository.SetArchivedAsync(id, userId, !isArchived);

        return RedirectAfterAction(id, returnTo, folder, refresh: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMailFolders(int id, List<int>? folderIds, string? returnTo, string? folder)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        await _mailRepository.SetMailFoldersAsync(id, userId, folderIds ?? new List<int>());
        return RedirectAfterAction(id, returnTo, folder, refresh: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMailFolder(string name, string? folder)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            await _mailRepository.CreateUserFolderAsync(userId, name.Trim());
        }

        var targetFolder = MailFolders.IsValidOrCustom(folder) ? folder! : MailFolders.Unassigned;
        return RedirectToAction(nameof(Index), new { folder = targetFolder });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMailFolder(int id, string? folder)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        await _mailRepository.DeleteUserFolderAsync(id, userId);

        // Silinen klasor su an acik klasorse (custom-{id}), Atanmamis'a don.
        var deletedFolderKey = MailFolders.CustomFolderPrefix + id;
        var targetFolder = string.Equals(folder, deletedFolderKey, StringComparison.Ordinal)
            ? MailFolders.Unassigned
            : (MailFolders.IsValidOrCustom(folder) ? folder! : MailFolders.Unassigned);

        return RedirectToAction(nameof(Index), new { folder = targetFolder });
    }

    [HttpGet]
    public async Task<IActionResult> GetAnnotations(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var strokesJson = await _mailRepository.GetAnnotationsAsync(id, userId);
        return Json(new { strokes = strokesJson ?? "[]" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAnnotations(int id, string strokesJson)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        await _mailRepository.SaveAnnotationsAsync(id, userId, strokesJson ?? "[]");
        return Ok();
    }

    // Herhangi bir mühendis, kendini veya (birden fazla secilebilen) ekip
    // üyelerini anında (onaysız) atayabilir.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignUser(int id, List<string>? userId, string? taskName, string? returnTo, string? folder)
    {
        var userIds = (userId ?? new List<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            return Forbid();
        }

        var actingUserId = _userManager.GetUserId(User);
        var acting = await _userManager.GetUserAsync(User);
        await _mailRepository.AssignUsersAsync(id, userIds, actingUserId, acting?.DisplayName, taskName);

        return RedirectAfterAction(id, returnTo, folder, refresh: true);
    }

    // Herhangi bir mühendis, herhangi bir atamayı anında (onaysız) kaldırabilir.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAssignedUser(int id, string userId, string? returnTo, string? folder)
    {
        await _mailRepository.RemoveAssignmentAsync(id, userId);
        return RedirectAfterAction(id, returnTo, folder, refresh: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCategoryMarker(int id, string? colorKey, string? returnTo, string? folder)
    {
        await _mailRepository.SetCategoryMarkerAsync(id, string.IsNullOrWhiteSpace(colorKey) ? null : colorKey);
        return RedirectAfterAction(id, returnTo, folder, refresh: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTags(int id, List<string>? tagTexts, List<string>? tagColors, string? returnTo, string? folder)
    {
        var userId = _userManager.GetUserId(User);
        var texts = tagTexts ?? new List<string>();
        var colors = tagColors ?? new List<string>();
        var tags = texts
            .Select((text, index) => (Text: text, ColorKey: index < colors.Count ? colors[index] : TagPalette.DefaultKey))
            .Where(t => !string.IsNullOrWhiteSpace(t.Text));

        await _mailRepository.SetTagsAsync(id, tags, userId);
        return RedirectAfterAction(id, returnTo, folder, refresh: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMail(int id, string? returnTo, string? folder)
    {
        var mail = await _mailRepository.GetByIdAsync(id);
        if (mail is not null)
        {
            DeleteAttachmentFiles(mail);
            await _mailRepository.DeleteMailAsync(id);
        }

        var targetFolder = MailFolders.IsValidOrCustom(folder) ? folder! : MailFolders.Unassigned;

        // Mail silindigi icin artik DetailFrame(id) yuklenemez (404 doner).
        // Bu yuzden iframe icindeysek, ust pencereyi (Index) dogrudan tazeleyen
        // kucuk bir "gecis" sayfasi donuyoruz.
        if (returnTo == "frame")
        {
            ViewData["Folder"] = targetFolder;
            return View("Deleted");
        }

        return RedirectToAction(nameof(Index), new { folder = targetFolder });
    }

    // Mail listesindeki satir secim kutulariyla secilen birden fazla maile ayni
    // islemi (atama/bayrak/belirtec/tag/klasor/arsiv/spam/silme) tek seferde uygular.
    // Tekil action'lardaki repository metotlarini her Id icin sirayla cagirir.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAction(
        List<int>? ids,
        string actionType,
        // Tekil action AssignUser gibi coklu secim: atama paneli artik checkbox
        // listesi oldugu icin ayni isimle birden fazla deger gelebilir.
        List<string>? userId,
        string? taskName,
        string? flagType,
        string? colorKey,
        List<string>? tagTexts,
        List<string>? tagColors,
        List<int>? folderIds,
        string? returnTo,
        string? folder)
    {
        var currentUserId = _userManager.GetUserId(User);
        var targetFolder = MailFolders.IsValidOrCustom(folder) ? folder! : MailFolders.Unassigned;

        if (currentUserId is null || ids is null || ids.Count == 0)
        {
            return RedirectToAction(nameof(Index), new { folder = targetFolder });
        }

        var requester = await _userManager.GetUserAsync(User);
        var displayName = requester?.DisplayName ?? string.Empty;

        // Her Id icin ayri ayri calisir ve tek bir mailde olusan hatayi (ornegin
        // beklenmedik null bir alan/kilitli bir ek dosyasi) yutup logluyor,
        // sonraki maillere devam ediyor. Oncesinde tek bir maildeki hata tum
        // dongudeki (ornegin 143 mailin) islenmemis kalanlarini sessizce iptal
        // ediyordu; kullanici "seçtiğim kadarını sil(emedi)" olarak yasiyordu.
        async Task RunPerIdAsync(Func<int, Task> action)
        {
            foreach (var id in ids)
            {
                try
                {
                    await action(id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Toplu işlem sırasında mail {MailId} için '{ActionType}' başarısız oldu, sıradaki maile devam ediliyor.", id, actionType);
                }
            }
        }

        // Bayrak islemlerinde kurala (bkz. MailFlagPolicy) takilan mailleri sayar; toplu
        // secimde bazi mailler uygun bazilari degil olabilecegi icin uygun olanlar
        // islenir, atlananlar icin kullaniciya tek bir ozet uyarisi gosterilir.
        var skippedByPolicy = 0;
        async Task ApplyFlagAsync(int id, string targetFlag)
        {
            var result = await _mailRepository.SetFlagAsync(id, targetFlag, currentUserId, displayName);
            if (result is MailFlagUpdateResult.NotAllowedUnassigned
                       or MailFlagUpdateResult.NotAllowedNotAssignee
                       or MailFlagUpdateResult.LockedByOtherUser)
            {
                skippedByPolicy++;
            }
        }

        switch (actionType)
        {
            case "assign":
                var assignUserIds = (userId ?? new List<string>())
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct()
                    .ToList();

                if (assignUserIds.Count > 0)
                {
                    await RunPerIdAsync(id => _mailRepository.AssignUsersAsync(id, assignUserIds, currentUserId, displayName, taskName));
                }
                break;

            case "flag":
                if (MailFlagTypes.IsValid(flagType))
                {
                    await RunPerIdAsync(id => ApplyFlagAsync(id, flagType!));
                }
                break;

            case "marker":
                await RunPerIdAsync(id => _mailRepository.SetCategoryMarkerAsync(id, string.IsNullOrWhiteSpace(colorKey) ? null : colorKey));
                break;

            case "tag":
                if (tagTexts is not null && tagTexts.Count > 0)
                {
                    var newTags = tagTexts
                        .Select((text, index) => (Text: text, ColorKey: tagColors is not null && index < tagColors.Count ? tagColors[index] : TagPalette.DefaultKey))
                        .Where(t => !string.IsNullOrWhiteSpace(t.Text))
                        .ToList();

                    if (newTags.Count > 0)
                    {
                        await RunPerIdAsync(async id =>
                        {
                            var mail = await _mailRepository.GetByIdAsync(id);
                            if (mail is null)
                            {
                                return;
                            }

                            var tags = mail.Tags.Select(t => (t.Text, t.ColorKey)).ToList();
                            tags.AddRange(newTags);
                            await _mailRepository.SetTagsAsync(id, tags, currentUserId);
                        });
                    }
                }
                break;

            case "addToFolder":
                if (folderIds is not null && folderIds.Count > 0)
                {
                    await RunPerIdAsync(async id =>
                    {
                        var existingFolderIds = await _mailRepository.GetMailFolderIdsAsync(id, currentUserId);
                        foreach (var folderId in folderIds)
                        {
                            existingFolderIds.Add(folderId);
                        }

                        await _mailRepository.SetMailFoldersAsync(id, currentUserId, existingFolderIds);
                    });
                }
                break;

            case "archive":
                await RunPerIdAsync(id => _mailRepository.SetArchivedAsync(id, currentUserId, true));
                break;

            case "spam":
                await RunPerIdAsync(id => ApplyFlagAsync(id, MailFlagTypes.Spam));
                break;

            case "unspam":
                await RunPerIdAsync(id => ApplyFlagAsync(id, MailFlagTypes.Active));
                break;

            case "delete":
                await RunPerIdAsync(async id =>
                {
                    var mail = await _mailRepository.GetByIdAsync(id);
                    if (mail is not null)
                    {
                        DeleteAttachmentFiles(mail);
                        await _mailRepository.DeleteMailAsync(id);
                    }
                });
                break;
        }

        if (skippedByPolicy > 0)
        {
            TempData["BulkFlagWarning"] =
                $"{skippedByPolicy} mail işaretlenmedi: görev üstünüzde değil ya da henüz kimseye atanmamış.";
        }

        return RedirectToAction(nameof(Index), new { folder = targetFolder });
    }

    private void DeleteAttachmentFiles(MailMessageModel mail)
    {
        foreach (var attachment in mail.Attachments)
        {
            DeleteFileIfExists(attachment.FilePath);
        }

        foreach (var reply in mail.Replies)
        {
            foreach (var attachment in reply.Attachments)
            {
                DeleteFileIfExists(attachment.FilePath);
            }
        }
    }

    private void DeleteFileIfExists(string relativePath)
    {
        try
        {
            var fullPath = Path.Combine(_environment.WebRootPath, relativePath);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ek dosyası silinemedi: {Path}", relativePath);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(int id, string body, string? returnTo, string? folder)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            await _mailRepository.ClearDraftAsync(id);
        }
        else
        {
            await _mailRepository.SaveDraftAsync(id, body, userId);
        }

        TempData["ReplySuccess"] = "Taslak kaydedildi.";
        return RedirectAfterAction(id, returnTo, folder, refresh: true);
    }

    // Taslaklar listesinden, maili silmeden sadece o maile bagli
    // yanit taslagini temizlemek icin kullanilir.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearMailDraft(int id, string? folder)
    {
        await _mailRepository.ClearDraftAsync(id);
        TempData["TemplateMessage"] = "Taslak silindi.";

        var targetFolder = MailFolders.IsValid(folder) ? folder! : MailFolders.Drafts;
        return RedirectToAction(nameof(Index), new { folder = targetFolder });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDraftTemplate(string title, string body, bool isPrivate = false)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body))
        {
            await _mailRepository.CreateDraftTemplateAsync(title.Trim(), body, userId, isPrivate);
            TempData["TemplateMessage"] = "Taslak oluşturuldu.";
        }

        return RedirectToAction(nameof(Index), new { folder = MailFolders.Drafts });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDraftTemplate(int id, string title, string body, bool isPrivate = false)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body))
        {
            await _mailRepository.UpdateDraftTemplateAsync(id, title.Trim(), body, userId, isPrivate);
            TempData["TemplateMessage"] = "Taslak güncellendi.";
        }

        return RedirectToAction(nameof(Index), new { folder = MailFolders.Drafts });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDraftTemplate(int id)
    {
        await _mailRepository.DeleteDraftTemplateAsync(id);
        TempData["TemplateMessage"] = "Taslak silindi.";
        return RedirectToAction(nameof(Index), new { folder = MailFolders.Drafts });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> SendReply(int id, string body, List<IFormFile>? attachments, string? returnTo, string? folder)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["ReplyError"] = "Yanıt metni boş olamaz.";
            return RedirectAfterAction(id, returnTo, folder, refresh: false);
        }

        var mail = await _mailRepository.GetByIdAsync(id);
        if (mail is null)
        {
            return NotFound();
        }

        var payloads = new List<ReplyAttachmentPayload>();
        if (attachments is not null)
        {
            foreach (var file in attachments.Where(f => f.Length > 0))
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                payloads.Add(new ReplyAttachmentPayload(
                    Path.GetFileName(file.FileName),
                    file.ContentType,
                    memoryStream.ToArray()));
            }
        }

        var sentOk = false;

        // Yaniti yazan muhendisin adi hem gonderilen e-postanin gonderen adinda
        // ve imzasinda hem de ekip ici bildirimde kullaniliyor.
        var senderDisplayName = (await _userManager.GetUserAsync(User))?.DisplayName ?? string.Empty;

        try
        {
            await _mailSenderService.SendReplyAsync(mail, body, payloads, senderDisplayName, HttpContext.RequestAborted);

            var replyId = await _mailRepository.InsertReplyAsync(new MailReply
            {
                MailMessageId = id,
                Body = body,
                SentByUserId = userId,
                SentAt = DateTimeOffset.UtcNow
            });

            foreach (var payload in payloads)
            {
                var relativeFolder = Path.Combine("uploads", "replies", replyId.ToString());
                var absoluteFolder = Path.Combine(_environment.WebRootPath, relativeFolder);
                Directory.CreateDirectory(absoluteFolder);

                var uniqueFileName = $"{Guid.NewGuid():N}_{payload.FileName}";
                var absolutePath = Path.Combine(absoluteFolder, uniqueFileName);
                await System.IO.File.WriteAllBytesAsync(absolutePath, payload.Content);

                await _mailRepository.InsertReplyAttachmentAsync(replyId, new MailReplyAttachment
                {
                    MailReplyId = replyId,
                    FileName = payload.FileName,
                    FilePath = Path.Combine(relativeFolder, uniqueFileName),
                    ContentType = payload.ContentType,
                    IsImage = payload.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                });
            }

            await _mailRepository.ClearDraftAsync(id);

            foreach (var assignee in mail.AssignedUsers.Where(a => a.UserId != userId))
            {
                await _messageRepository.SendMessageAsync(
                    userId,
                    senderDisplayName,
                    NotificationTextHelper.ReplySentText(mail.Subject),
                    assignee.UserId,
                    assignee.DisplayName);
            }

            TempData["ReplySuccess"] = "Yanıtınız gönderildi.";
            sentOk = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yanıt gönderilirken hata oluştu. MailId={MailId}", id);
            TempData["ReplyError"] = "Yanıt gönderilemedi. Lütfen SMTP ayarlarını kontrol edin.";
        }

        return RedirectAfterAction(id, returnTo, folder, refresh: sentOk);
    }

    // returnTo == "frame" ise istek DetailFrame (Index'in sağ paneldeki iframe'i) içinden geldi demektir.
    // refresh=true olan işlemler (atama/kapatma/spam/bayrak/taslak) mailin bulunduğu klasörü değiştirebilir,
    // bu yüzden doğrudan Index'e yönlendirmek yerine DetailFrame'e dönüp oradan üst pencereyi (Index) tazeliyoruz
    // (aksi halde Index, iframe'in İÇİNE yüklenip iç içe gömülü bir sayfa oluşurdu).
    private IActionResult RedirectAfterAction(int id, string? returnTo, string? folder, bool refresh)
    {
        if (returnTo == "frame")
        {
            return RedirectToAction(nameof(DetailFrame), new { id, folder, refresh });
        }

        return refresh
            ? RedirectToAction(nameof(Index), new { folder, selected = id })
            : RedirectToAction(nameof(Details), new { id });
    }
}
