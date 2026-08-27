using System.ComponentModel.DataAnnotations;

namespace task_list.Models;

public class LoginViewModel
{
    [Required]
    [Display(Name = "Kullanıcı Adı veya E-posta")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Şifre")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Beni hatırla")]
    public bool RememberMe { get; set; }
}

public class RegisterViewModel
{
    [Required]
    [Display(Name = "Ad Soyad")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Şifre")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Şifre Tekrar")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AccountSettingsViewModel
{
    [Required]
    [Display(Name = "Kullanıcı Adı")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Mevcut Şifre")]
    public string CurrentPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Yeni Şifre (opsiyonel)")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword))]
    [Display(Name = "Yeni Şifre (tekrar)")]
    public string? ConfirmNewPassword { get; set; }
}
