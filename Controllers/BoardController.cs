using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using task_list.Data;
using task_list.Models;
using task_list.Services;

namespace task_list.Controllers;

[Authorize]
public class BoardController : Controller
{
    private readonly IBoardRepository _boardRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBoardNotificationQueue _notificationQueue;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<BoardController> _logger;

    private static readonly HashSet<string> AllowedCoverImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".zip", ".txt"
    };

    // Kart aciklamasi/yorumlar zengin metin kutusundan (contenteditable) geliyor;
    // sunucuya kaydedilmeden once bu editorun uretebilecegi etiket/oznitelik
    // kumesiyle sinirlanip her turlu script/on* / javascript: enjeksiyonu temizlenir.
    private static readonly HtmlSanitizer RichTextSanitizer = CreateRichTextSanitizer();

    private static HtmlSanitizer CreateRichTextSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "s", "strike", "u", "code", "pre", "h1", "h2", "h3", "h4", "ul", "ol", "li", "a", "img", "blockquote", "div", "span", "hr" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "src", "alt", "title" })
        {
            sanitizer.AllowedAttributes.Add(attr);
        }

        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
        {
            sanitizer.AllowedSchemes.Add(scheme);
        }

        sanitizer.AllowedCssProperties.Clear();
        sanitizer.KeepChildNodes = true;
        return sanitizer;
    }

    public BoardController(
        IBoardRepository boardRepository,
        IMessageRepository messageRepository,
        UserManager<ApplicationUser> userManager,
        IBoardNotificationQueue notificationQueue,
        IWebHostEnvironment environment,
        ILogger<BoardController> logger)
    {
        _boardRepository = boardRepository;
        _messageRepository = messageRepository;
        _userManager = userManager;
        _notificationQueue = notificationQueue;
        _environment = environment;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var isCustomer = User.IsInRole("Customer");
        var boards = isCustomer
            ? await _boardRepository.GetBoardsForCustomerAsync(user.Id, NormalizeEmail(user.Email))
            : await _boardRepository.GetAllBoardsAsync();

        ViewData["CurrentUserId"] = user.Id;

        if (isCustomer && boards.Count > 0)
        {
            ViewData["CustomerSummary"] = await _boardRepository.GetCustomerBoardSummaryAsync(user.Id, NormalizeEmail(user.Email));
        }

        // Mühendis/Admin tarafinda, "Panolar" listesi cok fazla musteriden
        // gelen cok sayida panoyu bir arada gosterebildigi icin ust kisimda
        // musteriye gore filtreleme secenegi sunulur (bkz. Index.cshtml'deki
        // filtre penceresi); secim tamamen istemci tarafinda uygulanir.
        if (!isCustomer)
        {
            var customers = await _userManager.GetUsersInRoleAsync("Customer");
            ViewData["Customers"] = customers
                .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return View(boards);
    }

    /// <summary>
    /// "Panolarım" listesinden, pano sahibi musterinin panoya yeni yetkili
    /// e-postalar eklemesini saglar (Create ekranindaki chip girdisiyle ayni).
    /// </summary>
    [Authorize(Roles = "Customer")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAuthorizedEmails(int boardId, string? authorizedEmailsRaw)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        if (board is null || !string.Equals(board.CreatedByUserId, user.Id, StringComparison.Ordinal))
        {
            return Forbid();
        }

        var emails = (authorizedEmailsRaw ?? string.Empty)
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (emails.Length > 0)
        {
            await _boardRepository.AddAuthorizedEmailsAsync(boardId, emails);

            foreach (var email in emails.Select(e => e.Trim().ToLowerInvariant()).Distinct())
            {
                await TrySendEmailAsync(email, $"Yeni pano yetkisi: {board.Title}",
                    $"{user.DisplayName}, \"{board.Title}\" adlı bir görev panosuna erişim yetkisi verdi. " +
                    "Sisteme kayıt olup giriş yaparak panoya ulaşabilirsiniz.");
            }
        }

        if (IsAjaxRequest())
        {
            var updated = await _boardRepository.GetBoardDetailsAsync(boardId);
            return PartialView("_AuthorizedEmailsList", (boardId, updated?.AuthorizedEmails ?? new List<string>()));
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// "Panolarım" listesinden, pano sahibi musterinin halihazirda yetkili
    /// olan bir e-postayi panodan kaldirmasini saglar.
    /// </summary>
    [Authorize(Roles = "Customer")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAuthorizedEmail(int boardId, string email)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        if (board is null || !string.Equals(board.CreatedByUserId, user.Id, StringComparison.Ordinal))
        {
            return Forbid();
        }

        await _boardRepository.RemoveAuthorizedEmailAsync(boardId, email);

        if (IsAjaxRequest())
        {
            var updated = await _boardRepository.GetBoardDetailsAsync(boardId);
            return PartialView("_AuthorizedEmailsList", (boardId, updated?.AuthorizedEmails ?? new List<string>()));
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// "Panolar" listesindeki "..." menüsünün "Yetkileri Görüntüle" seçeneği
    /// (salt okunur): panoyu oluşturan müşteri ve bu müşterinin yetkilendirdiği
    /// diğer müşterilerin isim + e-postaları. Kayıtlı bir hesaba karşılık gelmeyen
    /// yetkili e-postalarda isim boş döner (görünümde "Kayıtlı değil" gösterilir).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> BoardAuthorizationsPanel(int boardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        if (board is null)
        {
            return NotFound();
        }

        var owner = await _userManager.FindByIdAsync(board.CreatedByUserId);

        var authorizedPeople = new List<BoardAuthorizedPersonViewModel>();
        foreach (var email in board.AuthorizedEmails)
        {
            var registered = await _userManager.FindByEmailAsync(email);
            authorizedPeople.Add(new BoardAuthorizedPersonViewModel
            {
                DisplayName = registered?.DisplayName,
                Email = email
            });
        }

        var model = new BoardAuthorizationsViewModel
        {
            OwnerDisplayName = board.CreatedByDisplayName,
            OwnerEmail = owner?.Email,
            AuthorizedPeople = authorizedPeople
        };

        return PartialView("_BoardAuthorizationsPanel", model);
    }

    /// <summary>
    /// "Panolarım" listesinden, pano sahibi musterinin panoyu (tum kartlari
    /// ve etiketleriyle birlikte) kalici olarak silmesini saglar.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBoard(int boardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        if (board is null)
        {
            return Forbid();
        }

        var isEngineerOrAdmin = User.IsInRole("Employee") || User.IsInRole("Admin");
        var isOwner = string.Equals(board.CreatedByUserId, user.Id, StringComparison.Ordinal);
        if (!isEngineerOrAdmin && !isOwner)
        {
            return Forbid();
        }

        await _boardRepository.DeleteBoardAsync(boardId);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// "Panolarım" listesinden, pano sahibi musterinin pano baslinigi
    /// yeniden adlandirmasini saglar.
    /// </summary>
    [Authorize(Roles = "Customer")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameBoard(int boardId, string title)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        if (board is null || !string.Equals(board.CreatedByUserId, user.Id, StringComparison.Ordinal))
        {
            return Forbid();
        }

        var trimmed = (title ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            await _boardRepository.RenameBoardAsync(boardId, trimmed.Length > 300 ? trimmed[..300] : trimmed);
        }

        return RedirectToAction(nameof(Index));
    }

    // Sablon farketmeksizin, uzerine aldigi (AssignedUserId) ve hala mühendis
    // aksiyonu bekleyen (bulundugu listeden mühendis rolüyle bir gecis olan)
    // tum kartlari listeler. Klasik'te bu her zaman Yapilacaklar listesidir
    // (Test'e tasindiktan sonra sira musteride oldugu icin listeden dusmesi
    // eski davranisla ayni); jenerik sablonlarda ise su an oturdugu listeden
    // mühendis rolüyle en az bir gecis varsa gosterilir.
    /// <param name="embedded">
    /// Yalnizca yerlesimi degistirir (ust cubuk/klasor menusu olmadan
    /// _FrameLayout); "İstatistiklerim" sayfasindaki "Bana Ait Kartlar" bolumu
    /// bu sayfayi gomulu calistirir. Kartlarin kendisi ve islevleri aynidir.
    /// </param>
    [Authorize(Roles = "Employee,Admin")]
    public async Task<IActionResult> MyTasks(bool embedded = false)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        ViewData["Embedded"] = embedded;

        var assigned = await _boardRepository.GetMyAssignedTasksAsync(userId);
        var items = new List<MyTaskItemViewModel>();

        foreach (var (card, templateKey) in assigned)
        {
            if (templateKey == BoardTemplates.Klasik)
            {
                if (card.ListKey != BoardLists.Todo)
                {
                    continue;
                }

                items.Add(new MyTaskItemViewModel
                {
                    Card = card,
                    TemplateKey = templateKey,
                    ListLabel = BoardLists.All.First(l => l.Key == BoardLists.Todo).Label,
                    Moves = new List<BoardListTransition>
                    {
                        new() { FromListKey = BoardLists.Todo, ToListKey = BoardLists.Test, AllowedRole = BoardAddCardRole.Engineer }
                    }
                });
                continue;
            }

            // Ayni kural "İstatistiklerim > Panolar > Bana Ait" sayacinda da kullanilir.
            if (!BoardTemplates.HasPendingEngineerAction(templateKey, card.ListKey))
            {
                continue;
            }

            var template = BoardTemplates.Get(templateKey);
            var listDef = template.GetList(card.ListKey)!;

            var moves = template.Transitions
                .Where(t => (t.FromListKey == "*" || t.FromListKey == card.ListKey) && t.AllowedRole == BoardAddCardRole.Engineer)
                .ToList();

            items.Add(new MyTaskItemViewModel
            {
                Card = card,
                TemplateKey = templateKey,
                ListLabel = listDef.Label,
                Moves = moves
            });
        }

        return View(items);
    }

    [Authorize(Roles = "Customer")]
    [HttpGet]
    public IActionResult Create(string? template)
    {
        var templateKey = BoardTemplates.IsValidKey(template) ? template! : BoardTemplates.Klasik;
        return View(new CreateBoardViewModel { TemplateKey = templateKey });
    }

    [Authorize(Roles = "Customer")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateBoardViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var authorizedEmails = (model.AuthorizedEmailsRaw ?? string.Empty)
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var templateKey = BoardTemplates.IsValidKey(model.TemplateKey) ? model.TemplateKey : BoardTemplates.Klasik;
        var boardId = await _boardRepository.CreateBoardAsync(model.Title, user.Id, user.DisplayName, authorizedEmails, templateKey);

        await NotifyEngineersAsync(NotificationTextHelper.BoardCreatedText(model.Title, user.DisplayName, templateKey));

        foreach (var email in authorizedEmails.Select(e => e.Trim().ToLowerInvariant()).Distinct())
        {
            await TrySendEmailAsync(email, $"Yeni pano: {model.Title}",
                $"{user.DisplayName}, \"{model.Title}\" adlı bir görev panosuna erişim yetkisi verdi. " +
                "Sisteme kayıt olup giriş yaparak panoya ulaşabilirsiniz.");
        }

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    /// <summary>
    /// "Pano Sablonlarim" bolumundeki goz ikonundan tetiklenir: secilen sablon
    /// icin gecici, tamamen isleyen bir onizleme panosu olusturup dogrudan
    /// Details sayfasina yonlendirir. Musteri ayni sablonu daha once onizlediyse
    /// eski onizleme panosu (ve tum degisiklikleri) once silinir.
    /// </summary>
    [Authorize(Roles = "Customer")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartPreview(string templateKey)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var resolvedTemplateKey = BoardTemplates.IsValidKey(templateKey) ? templateKey : BoardTemplates.Klasik;
        var boardId = await _boardRepository.StartPreviewBoardAsync(resolvedTemplateKey, user.Id, user.DisplayName);

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    /// <summary>
    /// Pano detayindaki "Onizlemeyi Durdur" butonundan tetiklenir: onizleme
    /// panosunu (tum gecici kartlariyla birlikte) kalici olarak siler.
    /// </summary>
    [Authorize(Roles = "Customer")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StopPreview(int boardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        if (board is null || !board.IsPreview || !string.Equals(board.CreatedByUserId, user.Id, StringComparison.Ordinal))
        {
            return Forbid();
        }

        await _boardRepository.DeletePreviewBoardAsync(boardId, user.Id);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id, int? round, int? highlightCardId)
    {
        var board = await _boardRepository.GetBoardDetailsAsync(id);
        if (board is null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var isEngineer = User.IsInRole("Employee") || User.IsInRole("Admin");
        if (!isEngineer)
        {
            var authorized = await _boardRepository.IsCustomerAuthorizedAsync(id, user.Id, NormalizeEmail(user.Email));
            if (!authorized)
            {
                return Forbid();
            }
        }

        ViewData["CanAddCard"] = !isEngineer;
        ViewData["IsEngineer"] = isEngineer;
        ViewData["IsAdmin"] = User.IsInRole("Admin");
        ViewData["CurrentUserId"] = user.Id;

        var viewedRound = board.Template.HasSprintRounds
            ? Math.Clamp(round ?? board.CurrentSprintRound, 1, board.CurrentSprintRound)
            : board.CurrentSprintRound;
        ViewData["ViewedRound"] = viewedRound;
        ViewData["IsReadOnlyRound"] = board.Template.HasSprintRounds && viewedRound < board.CurrentSprintRound;
        ViewData["HighlightCardId"] = highlightCardId;

        return View(board);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetListColor(int boardId, string listKey, string? color)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        if (!await CanAccessBoardAsync(boardId, user) || !BoardLists.IsValidColor(color))
        {
            return Forbid();
        }

        var templateKey = await _boardRepository.GetBoardTemplateKeyAsync(boardId);
        if (templateKey is null)
        {
            return NotFound();
        }

        if (templateKey == BoardTemplates.Klasik)
        {
            await _boardRepository.SetListColorAsync(boardId, listKey, color);
        }
        else
        {
            var template = BoardTemplates.Get(templateKey);
            if (!IsValidListKey(template, listKey))
            {
                return BadRequest();
            }

            await _boardRepository.SetGenericListColorAsync(boardId, listKey, color);
        }

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCard(int boardId, AddCardViewModel model, string? listKey)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var isEngineer = User.IsInRole("Employee") || User.IsInRole("Admin");
        var isCustomer = User.IsInRole("Customer") &&
            await _boardRepository.IsCustomerAuthorizedAsync(boardId, user.Id, NormalizeEmail(user.Email));

        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            var templateKey = await _boardRepository.GetBoardTemplateKeyAsync(boardId);

            if (templateKey is null)
            {
                return NotFound();
            }

            string addedListLabel;

            if (templateKey == BoardTemplates.Klasik)
            {
                if (!isCustomer)
                {
                    return Forbid();
                }

                await _boardRepository.AddCardAsync(boardId, model.Title.Trim(), model.Description, user.Id, user.DisplayName);
                addedListLabel = BoardLists.All.First(l => l.Key == BoardLists.Todo).Label;
            }
            else
            {
                if (!isCustomer && !isEngineer)
                {
                    return Forbid();
                }

                var template = BoardTemplates.Get(templateKey);
                var list = template.GetList(listKey);
                var actorRole = isEngineer ? BoardAddCardRole.Engineer : BoardAddCardRole.Customer;
                if (list is null || list.AddCardRole != actorRole)
                {
                    return Forbid();
                }

                await _boardRepository.AddCardToListAsync(boardId, list.Key, model.Title.Trim(), model.Description, user.Id, user.DisplayName);
                addedListLabel = list.Label;

                if (template.HasSprintRounds)
                {
                    await _boardRepository.TryAdvanceSprintRoundAsync(boardId);
                }
            }

            await NotifyCardAddedAsync(boardId, model.Title.Trim(), addedListLabel, user);
        }

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    // Klasik'te sadece Yapilacaklar listesinde, jenerik sablonlarda ise mühendis
    // rolündeki bir gecisin kaynak listesi oldugu her yerde bir mühendisin karti
    // "üstüne almasini" saglar (once uzerine alma, sonra siradaki listeye tasima
    // secenegi cikmasi kurali tum sablonlarda ayni).
    [Authorize(Roles = "Employee,Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignToMe(int cardId, int boardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        var templateKey = await _boardRepository.GetBoardTemplateKeyAsync(boardId);
        if (templateKey is null)
        {
            return NotFound();
        }

        var isEligibleList = templateKey == BoardTemplates.Klasik
            ? card.ListKey == BoardLists.Todo
            : BoardTemplates.Get(templateKey).Transitions.Any(t =>
                (t.FromListKey == "*" || t.FromListKey == card.ListKey) && t.AllowedRole == BoardAddCardRole.Engineer);

        if (!isEligibleList)
        {
            return Forbid();
        }

        var claimed = await _boardRepository.AssignCardToSelfAsync(cardId, user.Id, user.DisplayName);

        if (IsAjaxRequest())
        {
            return claimed ? Ok() : BadRequest("Bu kart az önce başka bir mühendis tarafından üstlenildi.");
        }

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    /// <summary>
    /// Bir mühendisin üzerine aldigi karti birakmasini (AssignToMe'nin tersi)
    /// saglar. Sadece kartin ilk kez uzerine alindigi listede kaldigi surece
    /// mumkundur (kart.AssignedListKey ile kart.ListKey ayni oldugu surece);
    /// mühendis karti sonraki bir listeye tasidiktan sonra artik birakamaz -
    /// bu kural hem burada hem de ReleaseCardAssignmentAsync icinde (savunma
    /// amacli, ikinci kez) dogrulanir.
    /// </summary>
    [Authorize(Roles = "Employee,Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleaseFromMe(int cardId, int boardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        if (card.AssignedUserId != user.Id || card.ListKey != card.AssignedListKey)
        {
            return Forbid();
        }

        var released = await _boardRepository.ReleaseCardAssignmentAsync(cardId, user.Id);

        if (IsAjaxRequest())
        {
            return released ? Ok() : BadRequest("Bu kart artık üzerinizde değil.");
        }

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    [Authorize(Roles = "Employee,Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveToTest(int cardId, int boardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var moved = await _boardRepository.MoveCardToTestAsync(cardId, user.Id, User.IsInRole("Admin"));
        if (moved)
        {
            var card = await _boardRepository.GetCardByIdAsync(cardId);
            var board = await _boardRepository.GetBoardDetailsAsync(boardId);
            if (card is not null && board is not null && !board.IsPreview)
            {
                await NotifyEngineersAsync(NotificationTextHelper.CardMovedToTestEngineerText(board.Title, card.Title));

                var customerText = NotificationTextHelper.CardMovedToTestText(board.Title, card.Title);
                await NotifyBoardAudienceAsync(board, user.Id, user.DisplayName,
                    $"Teste taşındı: {card.Title}", customerText, excludeEmail: null);
            }
        }
        else if (IsAjaxRequest())
        {
            return BadRequest();
        }

        if (IsAjaxRequest())
        {
            return Ok();
        }

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int cardId, int boardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var authorized = User.IsInRole("Customer") &&
            await _boardRepository.IsCustomerAuthorizedAsync(boardId, user.Id, NormalizeEmail(user.Email));
        if (!authorized)
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        await _boardRepository.ApproveCardAsync(cardId, user.Id);

        if (card is not null && card.AssignedUserId is not null && board is not null && !board.IsPreview)
        {
            await _messageRepository.SendMessageAsync(
                user.Id,
                user.DisplayName,
                NotificationTextHelper.CardApprovedText(board.Title, card.Title),
                card.AssignedUserId,
                card.AssignedUserDisplayName);
        }

        if (IsAjaxRequest())
        {
            return Ok();
        }

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int cardId, int boardId, RejectCardViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var authorized = User.IsInRole("Customer") &&
            await _boardRepository.IsCustomerAuthorizedAsync(boardId, user.Id, NormalizeEmail(user.Email));
        if (!authorized)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(model.Note))
        {
            if (IsAjaxRequest())
            {
                return BadRequest("Reddetmek için sorunu açıklamanız gerekiyor.");
            }

            TempData["BoardError"] = "Reddetmek için sorunu açıklamanız gerekiyor.";
            return RedirectToAction(nameof(Details), new { id = boardId });
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        await _boardRepository.RejectCardAsync(cardId, model.Note.Trim(), user.Id);
        await LogRejectionCommentAsync(cardId, user.DisplayName, model.Note.Trim());

        if (card is not null && board is not null && !board.IsPreview)
        {
            var text = NotificationTextHelper.CardRejectedText(board.Title, card.Title, model.Note.Trim());
            await NotifyEngineersAsync(text);
            await NotifyBoardAudienceAsync(board, user.Id, user.DisplayName,
                $"Reddedildi: {card.Title}", text, excludeEmail: NormalizeEmail(user.Email));
        }

        if (IsAjaxRequest())
        {
            return Ok();
        }

        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    /// <summary>
    /// Surukle-birak sonrasi tasinan karti, yeni liste/rol baglaminda dogru
    /// aksiyon butonlariyla yeniden cizip JS'in sayfa yenilemeden DOM'a
    /// yerlestirebilmesi icin kullanilir (_BoardCardItem partial'i).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CardFragment(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        card.Labels = await _boardRepository.GetLabelsForCardAsync(boardId, cardId);

        var isEngineer = User.IsInRole("Employee") || User.IsInRole("Admin");
        var canAddCard = !isEngineer;

        var templateKey = await _boardRepository.GetBoardTemplateKeyAsync(boardId);
        if (templateKey is not null && templateKey != BoardTemplates.Klasik)
        {
            var template = BoardTemplates.Get(templateKey);
            var isCustomer = User.IsInRole("Customer");
            var isAdmin = User.IsInRole("Admin");
            return PartialView("_GenericBoardCardItem", (card, boardId, template, isEngineer, isCustomer, false, isAdmin, user.Id));
        }

        return PartialView("_BoardCardItem", (card, boardId, card.ListKey, isEngineer, canAddCard, user.Id));
    }

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    [HttpGet]
    public async Task<IActionResult> CardOptionsPanel(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var deleteCheck = await CanDeleteCardAsync(boardId, cardId, user.Id);
        ViewData["BoardId"] = boardId;
        ViewData["CardId"] = cardId;
        ViewData["CanDeleteCard"] = deleteCheck.CanDelete;
        ViewData["CannotDeleteCardReason"] = deleteCheck.Reason;
        return PartialView("_CardOptionsPanel");
    }

    /// <summary>
    /// Bir kullanicinin bir karti silebilip silemeyecegini belirler: sadece
    /// kartin su an bulundugu listede kart EKLEME yetkisi olan rol (Musteri/
    /// Mühendis) o listedeki kartlari silebilir (tasima yetkisi yeterli degil).
    /// Klasik sablonda da ayni kural gecerlidir (Yapilacaklar = Musteri, digger
    /// listelerde kimse kart eklemedigi icin silme de yok).
    /// Mühendis rolünde ayrica: bu listelerde "üstüne alma" (AssignToMe) akisi
    /// oldugu icin bir mühendis, baska bir mühendisin uzerine aldigi karti
    /// silemez; ancak kendi uzerine aldigi (ya da henuz kimseye atanmamis)
    /// karti silebilir. Admin, atanmis olsun olmasin her zaman silebilir
    /// (diger yerlerdeki Admin istisnasiyla tutarli, bkz. MoveCardWithTransitionAsync).
    /// </summary>
    private async Task<(bool CanDelete, string Reason)> CanDeleteCardAsync(int boardId, int cardId, string currentUserId)
    {
        const string noPermissionReason = "Bu listede kart ekleme yetkiniz olmadığı için kartı silemezsiniz.";
        const string claimedBySomeoneElseReason = "Bu kartı başka bir mühendis üzerine aldığı için silemezsiniz.";

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return (false, noPermissionReason);
        }

        var templateKey = await _boardRepository.GetBoardTemplateKeyAsync(boardId);
        if (templateKey is null)
        {
            return (false, noPermissionReason);
        }

        var isAdmin = User.IsInRole("Admin");
        var isEngineer = isAdmin || User.IsInRole("Employee");
        var actorRole = isEngineer ? BoardAddCardRole.Engineer : BoardAddCardRole.Customer;

        var listDef = BoardTemplates.Get(templateKey).GetList(card.ListKey);
        if (listDef is null || listDef.AddCardRole != actorRole)
        {
            return (false, noPermissionReason);
        }

        if (actorRole == BoardAddCardRole.Engineer && !isAdmin && card.IsAssigned && card.AssignedUserId != currentUserId)
        {
            return (false, claimedBySomeoneElseReason);
        }

        return (true, string.Empty);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCard(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var deleteCheck = await CanDeleteCardAsync(boardId, cardId, user.Id);
        if (!deleteCheck.CanDelete)
        {
            return Forbid();
        }

        await _boardRepository.DeleteCardAsync(cardId);
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> CardLabelsPanel(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        var model = new CardLabelsPanelModel
        {
            BoardId = boardId,
            CardId = cardId,
            // Panelde secili/secisiz tum etiketler listelenir (varsayilan uc renk dahil).
            Labels = await _boardRepository.GetCardLabelPaletteAsync(boardId, cardId)
        };

        return PartialView("_CardLabelsPanel", model);
    }

    /// <summary>
    /// Etiketler ekranindaki kutucuk: etiketi kartta goster/gizle. Etiketi silmez,
    /// yalnizca secim durumunu degistirir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLabel(int boardId, int cardId, int labelId, bool selected)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        var label = await _boardRepository.GetLabelByIdAsync(labelId);
        if (label is null || label.CardId != cardId)
        {
            return NotFound();
        }

        if (!await _boardRepository.SetLabelSelectedAsync(labelId, cardId, selected, user.Id))
        {
            return NotFound();
        }

        return Json(new { labelId, selected, name = label.Name, color = label.Color });
    }

    [HttpGet]
    public async Task<IActionResult> LabelEditorPanel(int boardId, int cardId, int? labelId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var model = new LabelEditorPanelModel { BoardId = boardId, CardId = cardId, LabelId = labelId };

        if (labelId is int id)
        {
            var label = await _boardRepository.GetLabelByIdAsync(id);
            if (label is null || label.BoardId != boardId || label.CardId != cardId)
            {
                return NotFound();
            }

            model.Name = label.Name;
            model.Color = label.Color;
        }

        return PartialView("_LabelEditorPanel", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLabel(int boardId, int cardId, int? labelId, string name, string color)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        if (!BoardLabelColors.IsValidColor(color))
        {
            return BadRequest();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        name = (name ?? string.Empty).Trim();

        if (labelId is int id)
        {
            var updated = await _boardRepository.UpdateLabelAsync(id, cardId, name, color, user.Id);
            if (!updated)
            {
                return NotFound();
            }

            // Duzenlemek etiketi karta EKLEMEZ; gorunurlugu yalnizca kutucuk belirler.
            var existing = await _boardRepository.GetLabelByIdAsync(id);
            return Json(new { labelId = id, name, color, selected = existing?.IsSelected ?? false });
        }

        var created = await _boardRepository.CreateLabelForCardAsync(boardId, cardId, name, color, user.Id);
        return Json(new { labelId = created.Id, name = created.Name, color = created.Color, selected = created.IsSelected });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLabel(int boardId, int cardId, int labelId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var deleted = await _boardRepository.DeleteLabelAsync(labelId, cardId);
        if (!deleted)
        {
            return NotFound();
        }

        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> CardCoverPanel(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        var model = new CardCoverPanelModel
        {
            BoardId = boardId,
            CardId = cardId,
            CoverColor = card.CoverColor,
            CoverImagePath = card.CoverImagePath
        };

        return PartialView("_CardCoverPanel", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCardCoverColor(int boardId, int cardId, string color)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        if (!BoardCoverColors.IsValidColor(color))
        {
            return BadRequest();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        await _boardRepository.SetCardCoverColorAsync(cardId, color);
        return Json(new { cardId, coverColor = color, coverImagePath = (string?)null });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCardCoverPreset(int boardId, int cardId, string presetKey)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        if (!BoardCoverPresets.TryGetPath(presetKey, out var path))
        {
            return BadRequest();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        await _boardRepository.SetCardCoverImageAsync(cardId, path);
        return Json(new { cardId, coverColor = (string?)null, coverImagePath = path });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadCardCover(int boardId, int cardId, IFormFile file)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest();
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedCoverImageExtensions.Contains(extension))
        {
            return BadRequest();
        }

        var relativeFolder = Path.Combine("uploads", "board-covers", cardId.ToString());
        var absoluteFolder = Path.Combine(_environment.WebRootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteFolder, uniqueFileName);
        using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = "/" + Path.Combine(relativeFolder, uniqueFileName).Replace('\\', '/');

        await _boardRepository.SetCardCoverImageAsync(cardId, relativePath);
        return Json(new { cardId, coverColor = (string?)null, coverImagePath = relativePath });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadRichTextImage(int boardId, int cardId, IFormFile file)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest();
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedCoverImageExtensions.Contains(extension))
        {
            return BadRequest();
        }

        var relativeFolder = Path.Combine("uploads", "board-rte-images", cardId.ToString());
        var absoluteFolder = Path.Combine(_environment.WebRootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteFolder, uniqueFileName);
        using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = "/" + Path.Combine(relativeFolder, uniqueFileName).Replace('\\', '/');
        return Json(new { url = relativePath });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearCardCover(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        await _boardRepository.ClearCardCoverAsync(cardId);
        return Json(new { cardId, coverColor = (string?)null, coverImagePath = (string?)null });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveCard(int boardId, int cardId, string targetListKey, int targetPosition, string? note)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var templateKey = await _boardRepository.GetBoardTemplateKeyAsync(boardId);
        if (templateKey is null)
        {
            return NotFound();
        }

        var template = BoardTemplates.Get(templateKey);
        if (!IsValidListKey(template, targetListKey))
        {
            return BadRequest();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        if (templateKey == BoardTemplates.Klasik)
        {
            await _boardRepository.MoveCardAsync(cardId, boardId, targetListKey, Math.Max(1, targetPosition));

            var klasikTargetLabel = BoardLists.All.First(l => l.Key == targetListKey).Label;
            await NotifyCardMovedAsync(boardId, card.Title, klasikTargetLabel, isGateList: false, transition: null, note: null, user);

            return Ok();
        }

        var isEngineer = User.IsInRole("Employee") || User.IsInRole("Admin");
        var isAdmin = User.IsInRole("Admin");
        var result = await _boardRepository.MoveCardWithTransitionAsync(cardId, boardId, targetListKey, user.Id, isEngineer, isAdmin, note);

        if (result == BoardMoveResult.Success)
        {
            var targetList = template.GetList(targetListKey);
            if (targetList is not null)
            {
                var isGateList = template.Transitions.Any(t =>
                        string.Equals(t.FromListKey, targetListKey, StringComparison.Ordinal) &&
                        t.AllowedRole == BoardAddCardRole.Customer) ||
                    (template.HasSprintRounds && string.Equals(targetListKey, "sprint-done", StringComparison.Ordinal));

                var actorRole = isEngineer ? BoardAddCardRole.Engineer : BoardAddCardRole.Customer;
                var usedTransition = template.FindTransition(card.ListKey, targetListKey, actorRole);

                await NotifyCardMovedAsync(boardId, card.Title, targetList.Label, isGateList, usedTransition, note, user);

                if (usedTransition?.IsRejection == true && !string.IsNullOrWhiteSpace(note))
                {
                    await LogRejectionCommentAsync(cardId, user.DisplayName, note.Trim());
                }
            }

            if (template.HasSprintRounds)
            {
                await _boardRepository.TryAdvanceSprintRoundAsync(boardId);
            }

            return IsAjaxRequest() ? Ok() : RedirectToAction(nameof(Details), new { id = boardId });
        }

        if (IsAjaxRequest())
        {
            return result switch
            {
                BoardMoveResult.NoteRequired => BadRequest("Bu taşıma için bir açıklama/gerekçe girmeniz gerekiyor."),
                BoardMoveResult.RequiresAssignment => BadRequest("Bu kartı taşımadan önce üstünüze almanız gerekiyor."),
                BoardMoveResult.NotFound => NotFound(),
                _ => Forbid()
            };
        }

        TempData["BoardError"] = result switch
        {
            BoardMoveResult.NoteRequired => "Bu taşıma için bir açıklama/gerekçe girmeniz gerekiyor.",
            BoardMoveResult.RequiresAssignment => "Bu kartı taşımadan önce üstünüze almanız gerekiyor.",
            _ => "Bu taşıma işlemine yetkiniz yok."
        };
        return RedirectToAction(nameof(Details), new { id = boardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCardApproval(int boardId, int cardId, string status, string? note)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var authorized = User.IsInRole("Customer") &&
            await _boardRepository.IsCustomerAuthorizedAsync(boardId, user.Id, NormalizeEmail(user.Email));
        if (!authorized)
        {
            return Forbid();
        }

        if (status != "Approved" && status != "Rejected")
        {
            return BadRequest();
        }

        if (status == "Rejected" && string.IsNullOrWhiteSpace(note))
        {
            if (IsAjaxRequest())
            {
                return BadRequest("Reddetmek için bir açıklama girmeniz gerekiyor.");
            }

            TempData["BoardError"] = "Reddetmek için bir açıklama girmeniz gerekiyor.";
            return RedirectToAction(nameof(Details), new { id = boardId });
        }

        var templateKey = await _boardRepository.GetBoardTemplateKeyAsync(boardId);
        var template = BoardTemplates.Get(templateKey);
        if (!template.HasSprintRounds)
        {
            return BadRequest();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId || card.ListKey != "sprint-done")
        {
            return NotFound();
        }

        await _boardRepository.SetCardApprovalStatusAsync(cardId, status, user.Id);
        await _boardRepository.TryAdvanceSprintRoundAsync(boardId);

        if (status == "Rejected" && note is not null)
        {
            await LogRejectionCommentAsync(cardId, user.DisplayName, note.Trim());
        }

        return IsAjaxRequest() ? Ok() : RedirectToAction(nameof(Details), new { id = boardId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ArchiveCard(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        await _boardRepository.ArchiveCardAsync(cardId, user.Id);
        return Ok();
    }

    public async Task<IActionResult> Archive()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var cards = await _boardRepository.GetArchivedCardsForUserAsync(userId);

        foreach (var card in cards)
        {
            card.Labels = await _boardRepository.GetLabelsForCardAsync(card.BoardId, card.Id);
        }

        return View(cards);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreCard(int cardId)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || !string.Equals(card.ArchivedByUserId, userId, StringComparison.Ordinal))
        {
            return NotFound();
        }

        await _boardRepository.RestoreCardAsync(cardId);
        return RedirectToAction(nameof(Archive));
    }

    [HttpGet]
    public async Task<IActionResult> CardDetail(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        card.Labels = await _boardRepository.GetLabelsForCardAsync(boardId, cardId);

        var attachments = await _boardRepository.GetAttachmentsForCardAsync(cardId);
        var comments = await _boardRepository.GetCommentsForCardAsync(cardId);

        var reactions = await _boardRepository.GetReactionsForCardAsync(cardId);
        var reactionsByComment = reactions.GroupBy(r => r.CommentId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var comment in comments)
        {
            if (reactionsByComment.TryGetValue(comment.Id, out var commentReactions))
            {
                comment.Reactions = commentReactions;
            }
        }

        var model = new CardDetailViewModel
        {
            BoardId = boardId,
            Card = card,
            Attachments = attachments,
            Comments = comments,
            CurrentUserId = user.Id,
            CurrentUserDisplayName = user.DisplayName,
            CurrentUserRole = GetRoleLabel()
        };

        return PartialView("_CardDetailModal", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCardDescription(int boardId, int cardId, string? description)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        var sanitized = string.IsNullOrWhiteSpace(description) ? null : RichTextSanitizer.Sanitize(description);
        await _boardRepository.UpdateCardDescriptionAsync(cardId, sanitized);

        return Json(new { description = sanitized });
    }

    [HttpGet]
    public async Task<IActionResult> CardAttachmentPanel(int boardId, int cardId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        var model = new CardAttachmentPanelModel { BoardId = boardId, CardId = cardId };
        return PartialView("_CardAttachmentPanel", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCardAttachmentLink(int boardId, int cardId, string url)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest();
        }

        var attachment = await _boardRepository.AddCardAttachmentLinkAsync(cardId, uri.ToString(), user.Id, user.DisplayName);
        return Json(ToAttachmentDto(attachment));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadCardAttachmentFile(int boardId, int cardId, IFormFile file)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest();
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedAttachmentExtensions.Contains(extension))
        {
            return BadRequest();
        }

        var relativeFolder = Path.Combine("uploads", "board-attachments", cardId.ToString());
        var absoluteFolder = Path.Combine(_environment.WebRootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var originalFileName = Path.GetFileName(file.FileName);
        var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteFolder, uniqueFileName);
        using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = "/" + Path.Combine(relativeFolder, uniqueFileName).Replace('\\', '/');
        var attachment = await _boardRepository.AddCardAttachmentFileAsync(cardId, relativePath, originalFileName, user.Id, user.DisplayName);
        return Json(ToAttachmentDto(attachment));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCardAttachment(int boardId, int cardId, int attachmentId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var attachment = await _boardRepository.GetAttachmentByIdAsync(attachmentId);
        if (attachment is null || attachment.CardId != cardId)
        {
            return NotFound();
        }

        await _boardRepository.DeleteCardAttachmentAsync(attachmentId);
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCardComment(int boardId, int cardId, string body)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var card = await _boardRepository.GetCardByIdAsync(cardId);
        if (card is null || card.BoardId != boardId)
        {
            return NotFound();
        }

        var sanitized = RichTextSanitizer.Sanitize(body ?? string.Empty);
        if (string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(sanitized, "<[^>]+>", "")))
        {
            return BadRequest();
        }

        var comment = await _boardRepository.AddCardCommentAsync(cardId, user.Id, user.DisplayName, GetRoleLabel(), sanitized);

        return Json(new
        {
            comment.Id,
            comment.UserId,
            comment.DisplayName,
            comment.Role,
            comment.Body,
            createdAt = comment.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            canEdit = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCardComment(int boardId, int cardId, int commentId, string body)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var comment = await _boardRepository.GetCommentByIdAsync(commentId);
        if (comment is null || comment.CardId != cardId)
        {
            return NotFound();
        }

        if (!string.Equals(comment.UserId, user.Id, StringComparison.Ordinal))
        {
            return Forbid();
        }

        var sanitized = RichTextSanitizer.Sanitize(body ?? string.Empty);
        if (string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(sanitized, "<[^>]+>", "")))
        {
            return BadRequest();
        }

        await _boardRepository.UpdateCardCommentAsync(commentId, sanitized);
        return Json(new { commentId, body = sanitized });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCommentReaction(int boardId, int cardId, int commentId, string emoji)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !await CanAccessBoardAsync(boardId, user))
        {
            return Forbid();
        }

        var comment = await _boardRepository.GetCommentByIdAsync(commentId);
        if (comment is null || comment.CardId != cardId)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > 8)
        {
            return BadRequest();
        }

        await _boardRepository.ToggleCommentReactionAsync(commentId, user.Id, user.DisplayName, emoji);
        var reactions = await _boardRepository.GetReactionsForCommentAsync(commentId);

        return Json(new
        {
            commentId,
            currentUserId = user.Id,
            reactions = reactions.Select(r => new { r.Emoji, r.UserId, r.DisplayName })
        });
    }

    private static object ToAttachmentDto(BoardCardAttachment attachment) => new
    {
        attachment.Id,
        attachment.AttachmentType,
        attachment.Url,
        attachment.FileName,
        displayName = attachment.DisplayName,
        isFile = attachment.IsFile,
        createdByDisplayName = attachment.CreatedByDisplayName,
        createdAt = attachment.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
    };

    private string GetRoleLabel() =>
        User.IsInRole("Admin") ? "Yönetici" : User.IsInRole("Employee") ? "Mühendis" : "Müşteri";

    /// <summary>
    /// Her red islemini (Klasik'in Test->Todo reddi, jenerik sablonlardaki not
    /// gerektiren red gecisleri, sprint'li sablondaki Sprint Done reddi) kartin
    /// yorum akisina otomatik bir "sistem" yorumu olarak ekler. Boylece mühendis,
    /// kartı her actığında tum red aciklama gecmisini (sadece en sonuncusunu
    /// degil) tek bir yerde, sablon farketmeksizin gorebilir. UserId gercek bir
    /// hesaba karsilik gelmedigi icin (BoardCardComments.UserId'de FK yok) bu
    /// yorumlar hicbir kullaniciya "duzenle" olarak gorunmez.
    /// </summary>
    private async Task LogRejectionCommentAsync(int cardId, string rejectedByDisplayName, string note)
    {
        var encodedName = System.Net.WebUtility.HtmlEncode(rejectedByDisplayName);
        var encodedNote = System.Net.WebUtility.HtmlEncode(note).Replace("\n", "<br>");
        var body = $"<p>🚫 <strong>{encodedName}</strong> reddetti:<br>{encodedNote}</p>";

        await _boardRepository.AddCardCommentAsync(cardId, "system", "Sistem", "Sistem", body);
    }

    private static bool IsValidListKey(BoardTemplateDefinition template, string? listKey) =>
        template.Lists.Any(l => string.Equals(l.Key, listKey, StringComparison.Ordinal));

    private async Task<Dictionary<string, int>> GetCardCountByListForTemplateAsync(int boardId, BoardTemplateDefinition template)
    {
        var counts = await _boardRepository.GetCardCountByListAsync(boardId);
        foreach (var list in template.Lists)
        {
            counts.TryAdd(list.Key, 0);
        }

        return counts;
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private async Task<bool> CanAccessBoardAsync(int boardId, ApplicationUser user)
    {
        if (User.IsInRole("Employee") || User.IsInRole("Admin"))
        {
            return true;
        }

        return await _boardRepository.IsCustomerAuthorizedAsync(boardId, user.Id, NormalizeEmail(user.Email));
    }

    /// <summary>
    /// Sablon farketmeksizin (Klasik dahil), bir listeye yeni kart eklendiginde
    /// tum muhendisleri, panoyu olusturan kisiyi ve tum yetkili hesaplari
    /// bilgilendirir.
    /// </summary>
    private async Task NotifyCardAddedAsync(int boardId, string cardTitle, string listLabel, ApplicationUser actingUser)
    {
        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        if (board is null || board.IsPreview)
        {
            return;
        }

        var text = NotificationTextHelper.CardAddedText(board.Title, cardTitle, listLabel);
        await NotifyEngineersAsync(text);
        await NotifyBoardAudienceAsync(board, actingUser.Id, actingUser.DisplayName,
            $"Yeni madde: {cardTitle}", text, excludeEmail: NormalizeEmail(actingUser.Email));
    }

    /// <summary>
    /// Sablon farketmeksizin (Klasik dahil), bir kart herhangi bir listeye
    /// tasindiginda tum muhendisleri, panoyu olusturan kisiyi ve tum yetkili
    /// hesaplari bilgilendirir. Musteri onayi gereken listelere (gate list)
    /// tasindiginda musteri tarafina "onayınız bekleniyor" eklenir.
    /// </summary>
    private async Task NotifyCardMovedAsync(int boardId, string cardTitle, string targetListLabel, bool isGateList, BoardListTransition? transition, string? note, ApplicationUser actingUser)
    {
        var board = await _boardRepository.GetBoardDetailsAsync(boardId);
        if (board is null || board.IsPreview)
        {
            return;
        }

        string engineerText;
        string customerText;
        string subject;

        if (transition?.IsRejection == true)
        {
            var text = NotificationTextHelper.CardRejectedText(board.Title, cardTitle, note ?? string.Empty);
            engineerText = text;
            customerText = text;
            subject = $"Reddedildi: {cardTitle}";
        }
        else if (transition?.IsApproval == true)
        {
            var text = NotificationTextHelper.CardApprovedGenericText(board.Title, cardTitle, targetListLabel);
            engineerText = text;
            customerText = text;
            subject = $"Onaylandı: {cardTitle}";
        }
        else
        {
            engineerText = NotificationTextHelper.CardMovedText(board.Title, cardTitle, targetListLabel);
            customerText = isGateList
                ? NotificationTextHelper.CardMovedToGateListCustomerText(board.Title, cardTitle, targetListLabel)
                : engineerText;
            subject = $"{NotificationTextHelper.CardMovedToGateListSubject(targetListLabel)}: {cardTitle}";
        }

        await NotifyEngineersAsync(engineerText);
        await NotifyBoardAudienceAsync(board, actingUser.Id, actingUser.DisplayName,
            subject, customerText, excludeEmail: NormalizeEmail(actingUser.Email));
    }

    /// <summary>
    /// Tum mühendislere uygulama-ici bildirim. Gonderim istek yolunda YAPILMAZ;
    /// kuyruga birakilir (bkz. BoardNotificationBackgroundService).
    /// </summary>
    private Task NotifyEngineersAsync(string text)
    {
        _notificationQueue.Enqueue(new BoardNotificationJob { EngineerText = text });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pano sahibi + tum yetkili e-postalara e-posta gonderir; bunlardan kayitli bir
    /// hesaba karsilik gelenlere ayrica uygulama-ici bildirim ekler. Gonderim istek
    /// yolunda YAPILMAZ: alici sayisi kadar SMTP el sikismasi kart ekleme/tasimayi
    /// dakikalarca bekletiyordu, is artik kuyruga birakilip arka planda tek bir SMTP
    /// baglantisi uzerinden yapiliyor.
    /// </summary>
    private Task NotifyBoardAudienceAsync(Board board, string? senderUserId, string senderDisplayName, string emailSubject, string text, string? excludeEmail)
    {
        _notificationQueue.Enqueue(new BoardNotificationJob
        {
            AudienceText = text,
            OwnerUserId = board.CreatedByUserId,
            AuthorizedEmails = board.AuthorizedEmails.ToList(),
            ExcludeEmail = excludeEmail,
            EmailSubject = emailSubject,
            SenderUserId = senderUserId,
            SenderDisplayName = senderDisplayName
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tekil bilgilendirme e-postasi (pano olusturma/yetkilendirme davetleri).
    /// Bu da istek yolunu bloklamamasi icin kuyruga birakilir.
    /// </summary>
    private Task TrySendEmailAsync(string toAddress, string subject, string body)
    {
        _notificationQueue.Enqueue(new BoardNotificationJob
        {
            AudienceText = body,
            AuthorizedEmails = new[] { toAddress },
            EmailSubject = subject,
            NotifyRegisteredUsersInApp = false
        });
        return Task.CompletedTask;
    }
}
