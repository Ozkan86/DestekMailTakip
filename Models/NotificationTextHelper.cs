namespace task_list.Models;

// Bildirim metinlerinde konu (subject) boş olabiliyor; boşken "" gibi çirkin bir
// ifade yerine "bir görev" gibi genel bir ifadeye düşen yardımcı metotlar.
public static class NotificationTextHelper
{
    public static string ReplySentText(string? subject) =>
        string.IsNullOrWhiteSpace(subject)
            ? "Bir görev için yanıt gönderildi."
            : $"\"{subject}\" görevi için bir yanıt gönderildi.";

    public static string InboundMailArrivedText(string? subject) =>
        string.IsNullOrWhiteSpace(subject)
            ? "Bir görevde yeni bir mail geldi."
            : $"\"{subject}\" görevinde yeni bir mail geldi.";

    public static string BoardCreatedText(string boardTitle, string ownerDisplayName, string? templateKey = null)
    {
        var entryList = BoardTemplates.Get(templateKey).Lists.FirstOrDefault(l => l.AddCardRole != BoardAddCardRole.None);
        var entryListLabel = entryList?.Label ?? "Yapılacaklar";
        return $"{ownerDisplayName} \"{boardTitle}\" adlı yeni bir pano oluşturdu. {entryListLabel} listesinden görev üstlenebilirsiniz.";
    }

    public static string CardMovedToTestText(string boardTitle, string cardTitle) =>
        $"\"{boardTitle}\" panosunda \"{cardTitle}\" maddesi teste taşındı, onayınız bekleniyor.";

    // Muhendislere gonderilen bildirimde "onayiniz bekleniyor" ifadesi yer
    // almamali; onay/red islemini musteri yapiyor, muhendis sadece bilgilendirilir.
    public static string CardMovedToTestEngineerText(string boardTitle, string cardTitle) =>
        $"\"{boardTitle}\" panosunda \"{cardTitle}\" maddesi teste taşındı.";

    public static string CardApprovedText(string boardTitle, string cardTitle) =>
        $"\"{boardTitle}\" panosunda \"{cardTitle}\" maddesi onaylandı ve tamamlandı.";

    public static string CardRejectedText(string boardTitle, string cardTitle, string note) =>
        $"\"{boardTitle}\" panosunda \"{cardTitle}\" maddesi reddedildi: {note}";

    // Klasik disi sablonlarda liste adlari serbest (Ingilizce) metin oldugu
    // icin "Deployed'e", "Prod'a" gibi doğru ek Türkçe ünlü uyumuna göre
    // hesaplanir (son ünlüye bakarak kalin/ince, kelime ünlüyle bitiyorsa
    // araya "y" kaynastirma harfi eklenir).
    private static string GetDativeSuffix(string label)
    {
        char? lastVowel = null;
        for (var i = label.Length - 1; i >= 0; i--)
        {
            if ("aeıioöuüAEIİOÖUÜ".IndexOf(label[i]) >= 0)
            {
                lastVowel = char.ToLowerInvariant(label[i]);
                break;
            }
        }

        var isBack = lastVowel is 'a' or 'ı' or 'o' or 'u';
        var endsWithVowel = label.Length > 0 && "aeıioöuüAEIİOÖUÜ".IndexOf(label[^1]) >= 0;

        if (endsWithVowel)
        {
            return isBack ? "'ya" : "'ye";
        }

        return isBack ? "'a" : "'e";
    }

    private static string ListArrivalPhrase(string listLabel) => $"{listLabel}{GetDativeSuffix(listLabel)} taşındı";

    /// <summary>
    /// Herhangi bir sablonda (Klasik dahil), bir kart herhangi bir listeye
    /// tasindiginda muhendislere (ve musteri onayi gerekmeyen tasimalarda
    /// musteri tarafina da) gonderilen genel bilgilendirme metni.
    /// </summary>
    public static string CardMovedText(string boardTitle, string cardTitle, string listLabel) =>
        $"\"{boardTitle}\" panosunda \"{cardTitle}\" maddesi {ListArrivalPhrase(listLabel)}.";

    /// <summary>
    /// Musteri onayi gereken bir listeye (ornegin Kanban DevBoard'da
    /// "Deployed", Site Reliability'de "Prod") kart tasindiginda musteri/
    /// yetkili e-postalara gonderilen, onay beklendigini belirten metin.
    /// </summary>
    public static string CardMovedToGateListCustomerText(string boardTitle, string cardTitle, string listLabel) =>
        $"\"{boardTitle}\" panosunda \"{cardTitle}\" maddesi {ListArrivalPhrase(listLabel)}, onayınız bekleniyor.";

    public static string CardMovedToGateListSubject(string listLabel) => $"{listLabel}{GetDativeSuffix(listLabel)} taşındı";

    /// <summary>
    /// Klasik disi sablonlarda, bir kart onay tasimasiyla (ornegin "Customer
    /// Preview" -> "Live") tasindiginda hem muhendislere hem musteri/yetkili
    /// tarafina gonderilen, onaylandigini ve hangi listeye tasindigini
    /// belirten metin.
    /// </summary>
    public static string CardApprovedGenericText(string boardTitle, string cardTitle, string targetListLabel) =>
        $"\"{boardTitle}\" panosunda \"{cardTitle}\" maddesi onaylandı ve \"{targetListLabel}\" listesine taşındı.";

    /// <summary>
    /// Herhangi bir sablonda (Klasik dahil), herhangi bir listeye yeni kart
    /// eklendiginde muhendislere ve musteri/yetkili tarafina gonderilen metin.
    /// </summary>
    public static string CardAddedText(string boardTitle, string cardTitle, string listLabel) =>
        $"\"{boardTitle}\" panosunda \"{listLabel}\" listesine yeni bir madde eklendi: \"{cardTitle}\"";
}
