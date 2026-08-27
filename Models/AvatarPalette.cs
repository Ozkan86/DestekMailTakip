namespace task_list.Models;

/// <summary>
/// Rozet (avatar) renklerini verir. Her kullaniciya veritabaninda saklanan bir
/// renk indeksi atanir (bkz. ApplicationUser.AvatarColorIndex) ve renk her zaman
/// bu indeksten okunur. Boylece ayni mühendis uygulamanin her yerinde (liste
/// satiri, atama menusu, mail detay konusmalari, pano kartlari) ayni rengi
/// kullanir.
///
/// Palet, arayuzun marka mavisiyle (--brand-blue: #235585) uyumlu, ayni
/// doygunluk/parlaklik ailesinden 8 tondan olusur. Ilk 8 mühendis birbirinden
/// farkli renk alir; 9. mühendisten itibaren palet basa doner (index % 8), yani
/// renkler tekrar kullanilir.
/// </summary>
public static class AvatarPalette
{
    /// <summary>Mühendis/kullanici rozet paleti (marka mavisiyle uyumlu 8 ton).</summary>
    private static readonly string[] Colors =
    {
        "#235585", // marka mavisi
        "#8c3b4a", // kiremit bordo
        "#3f7d6a", // adaçayı yeşili
        "#7a5c9b", // mor
        "#a06129", // amber/kehribar
        "#2f6f92", // petrol mavisi
        "#5f7a3a", // zeytin yeşili
        "#8a4f7d", // erik moru
    };

    /// <summary>Palette bulunan renk sayisi; bu sayidan sonra renkler tekrarlanir.</summary>
    public static int Count => Colors.Length;

    /// <summary>Verilen renk indeksi icin "#rrggbb" formatinda rozet rengi.</summary>
    public static string ColorFor(int index)
    {
        if (index < 0)
        {
            index = 0;
        }

        return Colors[index % Colors.Length];
    }

    /// <summary>
    /// Musteri gibi kullanici hesabi olmayan taraflar icin isim/adres tabanli
    /// sabit renk. Ayni paletin soluk (dusuk doygunluk) karsiliklari kullanilir;
    /// boylece musteri rozetleri arayuzle uyumlu kalirken canli renkli mühendis
    /// rozetlerinden gozle ayrilir.
    /// </summary>
    public static string ColorForSeed(string? seed)
    {
        var normalized = (seed ?? string.Empty).Trim().ToLowerInvariant();
        var hash = 17;
        foreach (var ch in normalized)
        {
            hash = unchecked(hash * 31 + ch);
        }

        return CustomerColors[Math.Abs(hash) % CustomerColors.Length];
    }

    /// <summary>Musteri rozetleri icin ayni ton ailesinin soluk karsiliklari.</summary>
    private static readonly string[] CustomerColors =
    {
        "#5a6b7d", // soluk mavi-gri
        "#7d6266", // soluk bordo-gri
        "#5b7169", // soluk yeşil-gri
        "#6e6478", // soluk mor-gri
        "#7d6a56", // soluk kahve-gri
        "#5c6f78", // soluk petrol-gri
        "#67705c", // soluk zeytin-gri
        "#75606f", // soluk erik-gri
    };
}
