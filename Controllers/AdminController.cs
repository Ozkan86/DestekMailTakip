using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using task_list.Data;
using task_list.Models;
using task_list.Services;

namespace task_list.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private const string EmployeeRole = "Employee";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMessageRepository _messageRepository;
    private readonly IUserAvatarColorService _avatarColors;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        IMessageRepository messageRepository,
        IUserAvatarColorService avatarColors)
    {
        _userManager = userManager;
        _messageRepository = messageRepository;
        _avatarColors = avatarColors;
    }

    public async Task<IActionResult> Employees()
    {
        var model = await BuildEmployeesPageViewModel();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEmployee(CreateEmployeeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var page = await BuildEmployeesPageViewModel();
            page.NewEmployee = model;
            return View(nameof(Employees), page);
        }

        var user = new ApplicationUser
        {
            UserName = model.UserName,
            DisplayName = model.DisplayName
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, EmployeeRole);
            // Yeni mühendise paletten bir sonraki bos rozet rengini ata.
            await _avatarColors.AssignColorIndexAsync(user);
            TempData["AdminMessage"] = "Çalışan oluşturuldu.";
            return RedirectToAction(nameof(Employees));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        var pageWithErrors = await BuildEmployeesPageViewModel();
        pageWithErrors.NewEmployee = model;
        return View(nameof(Employees), pageWithErrors);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEmployee(EditEmployeeViewModel model)
    {
        var user = await _userManager.FindByIdAsync(model.Id);
        if (user is null)
        {
            return NotFound();
        }

        user.DisplayName = model.DisplayName;
        await _userManager.UpdateAsync(user);

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
        }

        TempData["AdminMessage"] = "Çalışan güncellendi.";
        return RedirectToAction(nameof(Employees));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEmployee(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is not null)
        {
            await _userManager.DeleteAsync(user);
        }

        TempData["AdminMessage"] = "Çalışan silindi.";
        return RedirectToAction(nameof(Employees));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage(SendMessageViewModel model)
    {
        if (!model.SendToAll && !model.RecipientUserIds.Any())
        {
            ModelState.AddModelError(string.Empty, "Lütfen en az bir çalışan seçin veya 'Tüm çalışanlara gönder' seçeneğini işaretleyin.");
            var page = await BuildEmployeesPageViewModel();
            page.NewMessage = model;
            return View(nameof(Employees), page);
        }

        var admin = await _userManager.GetUserAsync(User);

        if (model.SendToAll)
        {
            // NULL RecipientUserId = "herkese yayin" anlamina geliyor (bkz. MessageRepository),
            // ve artik musteriler de ayni Messages tablosunu kullandigi icin bu, ic
            // duyurularin musterilere sizmasina yol acar. Bunun yerine her calisana
            // ayri bir satir ekleniyor (coklu-alici dalindaki ayni desen).
            var employees = await _userManager.GetUsersInRoleAsync(EmployeeRole);
            foreach (var employee in employees)
            {
                await _messageRepository.SendMessageAsync(
                    admin?.Id,
                    admin?.DisplayName ?? "Yönetici",
                    model.Body,
                    employee.Id,
                    employee.DisplayName);
            }

            TempData["AdminMessage"] = "Mesaj tüm çalışanlara gönderildi.";
        }
        else
        {
            foreach (var recipientUserId in model.RecipientUserIds.Distinct())
            {
                var recipient = await _userManager.FindByIdAsync(recipientUserId);
                await _messageRepository.SendMessageAsync(
                    admin?.Id,
                    admin?.DisplayName ?? "Yönetici",
                    model.Body,
                    recipientUserId,
                    recipient?.DisplayName);
            }

            TempData["AdminMessage"] = model.RecipientUserIds.Count == 1
                ? "Mesaj gönderildi."
                : $"Mesaj {model.RecipientUserIds.Count} çalışana gönderildi.";
        }

        return RedirectToAction(nameof(Employees));
    }

    private async Task<EmployeesPageViewModel> BuildEmployeesPageViewModel()
    {
        var employees = await _userManager.GetUsersInRoleAsync(EmployeeRole);

        return new EmployeesPageViewModel
        {
            Employees = employees
                .OrderBy(e => e.DisplayName)
                .Select(e => new EmployeeListItem
                {
                    Id = e.Id,
                    UserName = e.UserName ?? string.Empty,
                    DisplayName = e.DisplayName
                })
                .ToList()
        };
    }
}
