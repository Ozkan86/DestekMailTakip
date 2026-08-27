using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using task_list.Models;
using task_list.Services;

namespace task_list.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IUserAvatarColorService _avatarColors;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IUserAvatarColorService avatarColors)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _avatarColors = avatarColors;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Identity kullanici adi/eposta normalizasyonu (NormalizedUserName) sadece
        // buyuk/kucuk harfi esitler, bosluk kirpmaz; kopyala-yapistirdan gelen
        // basta/sonda bosluk kullaniciyi hep "hatali sifre" sonucuna dusurur.
        var userName = model.UserName.Trim();

        var result = await _signInManager.PasswordSignInAsync(userName, model.Password, model.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(userName);
            var isCustomer = user is not null && await _userManager.IsInRoleAsync(user, "Customer");

            // Musteriler sadece Board alanina erisebiliyor (Mail personele ozel). Musteri
            // icin Board disina isaret eden bir returnUrl'i (orn. anonim haldeyken tiklanmis
            // bir personel linkinden gelmis olabilir) izlemek, [Authorize(Roles=...)] onu
            // reddedip AccessDeniedPath'e (eskiden login sayfasiyla ayniydi) atacagindan,
            // "sifre doğru ama tekrar login ekranina dusuyor" gibi gorunen bir donguye yol acardi.
            if (isCustomer)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl.StartsWith("/Board", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Board");
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Mail");
        }

        ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = model.DisplayName.Trim()
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await _userManager.AddToRoleAsync(user, "Customer");
        await _avatarColors.AssignColorIndexAsync(user);
        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Board");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Settings()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        return View(new AccountSettingsViewModel { UserName = user.UserName ?? string.Empty });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(AccountSettingsViewModel model)
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

        if (!await _userManager.CheckPasswordAsync(user, model.CurrentPassword))
        {
            ModelState.AddModelError(string.Empty, "Mevcut şifre hatalı.");
            return View(model);
        }

        if (!string.Equals(user.UserName, model.UserName, StringComparison.Ordinal))
        {
            var setNameResult = await _userManager.SetUserNameAsync(user, model.UserName);
            if (!setNameResult.Succeeded)
            {
                foreach (var error in setNameResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }
        }

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var changePasswordResult = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!changePasswordResult.Succeeded)
            {
                foreach (var error in changePasswordResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["AccountMessage"] = "Hesap bilgileriniz güncellendi.";
        return RedirectToAction(nameof(Settings));
    }
}
