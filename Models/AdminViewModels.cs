using System.ComponentModel.DataAnnotations;

namespace task_list.Models;

public class EmployeeListItem
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class EmployeesPageViewModel
{
    public List<EmployeeListItem> Employees { get; set; } = new();
    public CreateEmployeeViewModel NewEmployee { get; set; } = new();
    public SendMessageViewModel NewMessage { get; set; } = new();
}

public class CreateEmployeeViewModel
{
    [Required]
    [Display(Name = "Kullanıcı Adı")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Ad Soyad")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Şifre")]
    public string Password { get; set; } = string.Empty;
}

public class EditEmployeeViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Ad Soyad")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Yeni Şifre (opsiyonel)")]
    public string? NewPassword { get; set; }
}

public class SendMessageViewModel
{
    [Display(Name = "Alıcılar")]
    public List<string> RecipientUserIds { get; set; } = new();

    [Display(Name = "Tüm çalışanlara gönder")]
    public bool SendToAll { get; set; }

    [Required]
    [Display(Name = "Mesaj")]
    public string Body { get; set; } = string.Empty;
}
