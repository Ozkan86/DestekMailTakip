using Microsoft.AspNetCore.Identity;

namespace task_list.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Kullaniciya ait rozet (avatar) renginin kalici indeksi. Her kullanici
    /// icin tekildir; -1 ise henuz atanmamis demektir ve ilk kullanimda
    /// IUserAvatarColorService tarafindan bos olan en kucuk indeks verilir.
    /// Renk bu indeksten okunur (bkz. AvatarPalette): ilk 8 kullanici farkli
    /// renk alir, sonrasinda palet basa doner.
    /// </summary>
    public int AvatarColorIndex { get; set; } = -1;
}
