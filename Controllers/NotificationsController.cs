using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using task_list.Data;
using task_list.Models;

namespace task_list.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly IMessageRepository _messageRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public NotificationsController(IMessageRepository messageRepository, UserManager<ApplicationUser> userManager)
    {
        _messageRepository = messageRepository;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var count = await _messageRepository.GetUnreadCountAsync(userId);
        return Json(new { count });
    }

    [HttpGet]
    public async Task<IActionResult> Recent()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var messages = await _messageRepository.GetVisibleMessagesAsync(userId);
        ViewData["CurrentUserId"] = userId;
        return PartialView("_NotificationList", messages);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int messageId, string body)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return BadRequest();
        }

        var original = await _messageRepository.GetByIdAsync(messageId);
        if (original is null || original.SenderUserId is null || original.SenderUserId == userId)
        {
            return BadRequest();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        await _messageRepository.SendMessageAsync(
            userId,
            currentUser?.DisplayName ?? string.Empty,
            body,
            original.SenderUserId,
            original.SenderDisplayName);

        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        await _messageRepository.MarkAllReadAsync(userId);
        return Ok();
    }
}
