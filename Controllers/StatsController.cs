using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using task_list.Data;
using task_list.Models;

namespace task_list.Controllers;

/// <summary>
/// Mühendis profil menüsündeki rozetten açılan "İstatistiklerim" sayfası:
/// mail (destek) ve pano istatistikleri + kullanıcıya özel notlar.
/// </summary>
[Authorize(Roles = "Employee,Admin")]
public class StatsController : Controller
{
    private readonly IStatsRepository _statsRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public StatsController(IStatsRepository statsRepository, UserManager<ApplicationUser> userManager)
    {
        _statsRepository = statsRepository;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        // Sayfayi goruntulemek = decay'li istatistikler icin "gordu" ani; bir sonraki
        // acilista bu andan itibaren 4 saat gecmemis olaylar hala sayilir.
        await _statsRepository.MarkAllStatEventsSeenAsync(user.Id);

        var model = new EngineerStatsPageViewModel
        {
            Mail = await _statsRepository.GetMailStatsAsync(user.Id),
            Board = await _statsRepository.GetBoardStatsAsync(user.Id),
            Notes = await _statsRepository.GetNotesAsync(user.Id)
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> MessagesDrawer()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var items = await _statsRepository.GetNewMessageTasksAsync(user.Id);
        return PartialView("_MessagesDrawer", items);
    }

    [HttpGet]
    public async Task<IActionResult> OtherApprovalsDrawer()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var items = await _statsRepository.GetCardEventsAsync(user.Id,
            new[] { EngineerStatKeys.CardApprovedOther, EngineerStatKeys.CardRejectedOther });
        return PartialView("_CardEventsDrawer", items);
    }

    [HttpGet]
    public async Task<IActionResult> MyApprovalsDrawer()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var items = await _statsRepository.GetCardEventsAsync(user.Id,
            new[] { EngineerStatKeys.CardApprovedMine, EngineerStatKeys.CardRejectedMine });
        return PartialView("_CardEventsDrawer", items);
    }

    [HttpGet]
    public async Task<IActionResult> LabelsCommentsDrawer()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var items = await _statsRepository.GetCardEventsAsync(user.Id,
            new[] { EngineerStatKeys.CardLabelAdded, EngineerStatKeys.CardCommentAdded });
        return PartialView("_CardEventsDrawer", items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(string body)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        body = (body ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(body))
        {
            return BadRequest();
        }

        if (body.Length > 2000)
        {
            body = body[..2000];
        }

        await _statsRepository.AddNoteAsync(user.Id, body);
        var notes = await _statsRepository.GetNotesAsync(user.Id);
        return PartialView("_NotesList", notes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNote(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        await _statsRepository.DeleteNoteAsync(id, user.Id);
        var notes = await _statsRepository.GetNotesAsync(user.Id);
        return PartialView("_NotesList", notes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditNote(int id, string body)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        body = (body ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(body))
        {
            return BadRequest();
        }

        if (body.Length > 2000)
        {
            body = body[..2000];
        }

        var updated = await _statsRepository.UpdateNoteAsync(id, user.Id, body);
        if (updated is null)
        {
            return NotFound();
        }

        return Json(new { id = updated.Id, body = updated.Body });
    }

    /// <summary>Notlarım sürükle-bırak sıralaması: yeni sırayı (en üstteki ilk) toplu olarak kaydeder.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderNotes(List<int> ids)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        if (ids is null || ids.Count == 0)
        {
            return BadRequest();
        }

        await _statsRepository.ReorderNotesAsync(user.Id, ids);
        return Ok();
    }
}
