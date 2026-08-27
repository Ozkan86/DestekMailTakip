namespace task_list.Models;

/// <summary>
/// "Kart" - ne oldugunu aciklayan tek kart, sonra iki ornek talep/gorev karti.
/// </summary>
public class PreviewSeedCard
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public PreviewSeedCard(string title, string? description = null)
    {
        Title = title;
        Description = description;
    }
}

/// <summary>
/// Sablon onizlemesi (bkz. BoardController.StartPreview) baslatildiginda her
/// listeye eklenen 3 sabit ornek kart: birincisi o listenin bu sablonda ne
/// ise yaradigini anlatir, digger ikisi o listeye gercekci dusecek kisa
/// ornek talep/gorev kartlaridir. Musteri gercek bir pano olusturmus gibi
/// bu kartlari surukleyip, duzenleyip, silebilir; onizleme durdurulunca
/// (ya da yeniden baslatilinca) hepsi sifirlanir.
/// </summary>
public static class BoardTemplatePreviewSeeds
{
    private const string PurposeTitle = "Bu liste ne için kullanılır?";

    private static PreviewSeedCard P(string description) => new(PurposeTitle, description);
    private static PreviewSeedCard E(string title) => new(title);

    private static readonly Dictionary<string, Dictionary<string, PreviewSeedCard[]>> Data = new()
    {
        [BoardTemplates.Klasik] = new()
        {
            [BoardLists.Todo] = new[]
            {
                P("Müşterinin eklediği tüm talep ve görevler önce burada listelenir. Bir mühendis işi üstlenip bitirdiğinde kart Test listesine taşınır."),
                E("Ana sayfadaki logo daha büyük olsun istiyorum."),
                E("Sipariş formuna telefon numarası alanı eklenebilir mi?")
            },
            [BoardLists.Test] = new[]
            {
                P("Mühendis tarafından tamamlanıp test edilmek üzere taşınan kartlar burada bekler. Sorun yoksa Tamamlanan listesine, sorun varsa tekrar Yapılacaklar'a döner."),
                E("Yeni giriş ekranı test edilmeyi bekliyor."),
                E("Rapor dışa aktarma özelliği test aşamasında.")
            },
            [BoardLists.Done] = new[]
            {
                P("Testi geçen ve müşteri tarafından kabul edilen tüm kartların son durağı."),
                E("Şifre sıfırlama e-postası tamamlandı."),
                E("Mobil görünüm düzeltmesi tamamlandı.")
            }
        },

        [BoardTemplates.SoftwareProjectManagement] = new()
        {
            ["about-project"] = new[]
            {
                P("Projenin genel tanımı, hedefleri ve kapsamı burada tutulur; ekibin projeye bakınca ilk göreceği listedir."),
                E("Proje kapsamı: stok takip modülünün yeniden yazılması."),
                E("Hedef: raporlama ekranlarının yükleme süresini yarıya indirmek.")
            },
            ["requirement-discussion"] = new[]
            {
                P("Müşteri taleplerinin mühendislerle birlikte netleştirildiği listedir; görüşme tamamlanınca mühendis kartı Approved Requests'e taşır."),
                E("Fatura ekranına PDF olarak dışa aktarma eklensin."),
                E("Kullanıcı rollerine göre menü öğeleri gizlenebilsin mi?")
            },
            ["approved-requests"] = new[]
            {
                P("Görüşmesi tamamlanıp onaylanan, geliştirilmeye hazır talepler burada bekler; bir mühendis üstüne alınca Work in Progress'e taşınır."),
                E("Onaylandı: raporlara tarih aralığı filtresi eklenmesi."),
                E("Onaylandı: e-posta bildirimlerinin Türkçeleştirilmesi.")
            },
            ["work-in-progress"] = new[]
            {
                P("Mühendisler tarafından Approved Requests listesinden taşınan kartlardan oluşur. Halihazırda çalıştıkları işler burada listelenir; iş bittiğinde mühendis tarafından Unit Testing listesine taşınır."),
                E("Yazara göre kitap filtreleyebilelim lütfen."),
                E("Bir de sisteme ilk giriş yaptığımda hoşgeldiniz yazısı olsun istiyorum.")
            },
            ["unit-testing"] = new[]
            {
                P("Geliştirmesi biten kartların birim testlerden geçtiği listedir; testler geçince Integration Testing'e taşınır."),
                E("Filtreleme fonksiyonunun birim testleri yazılıyor."),
                E("Hoşgeldin mesajı bileşeninin birim testleri yazılıyor.")
            },
            ["integration-testing"] = new[]
            {
                P("Birim testleri geçen kartların diğer modüllerle birlikte uçtan uca test edildiği listedir; ardından UAT Review'a taşınır."),
                E("Filtreleme özelliğinin arama ekranıyla birlikte entegrasyon testi."),
                E("Hoşgeldin mesajının bildirim sistemiyle çakışmadığının doğrulanması.")
            },
            ["uat-review"] = new[]
            {
                P("Müşterinin son kullanıcı kabul testini yaptığı listedir; müşteri kartı onaylarsa Documentation Review'a, sorun bulursa (açıklama girerek) Requirement Discussion'a geri döner."),
                E("Yazara göre filtreleme müşteri tarafından deneniyor."),
                E("Hoşgeldin yazısı müşteri onayına sunuldu.")
            },
            ["documentation-review"] = new[]
            {
                P("Müşteri onayı alınan kartlar için kullanım/teknik dokümantasyonun hazırlandığı ve gözden geçirildiği listedir; ardından Done'a taşınır."),
                E("Filtreleme özelliği için kullanıcı kılavuzu güncellendi."),
                E("Hoşgeldin ekranı için ekran görüntülü doküman hazırlandı.")
            },
            ["done"] = new[]
            {
                P("Dokümantasyonu da tamamlanan, projeye kalıcı olarak eklenen kartların son durağı."),
                E("Yazara göre kitap filtreleme özelliği canlıya alındı."),
                E("İlk girişte hoşgeldiniz yazısı canlıya alındı.")
            }
        },

        [BoardTemplates.KanbanDevBoard] = new()
        {
            ["requirements"] = new[]
            {
                P("Müşterinin ilettiği ham talepler önce burada toplanır; bir mühendis talebi değerlendirip Backlog'a taşır."),
                E("Ürün listesine stok durumu etiketi eklensin."),
                E("Sepet sayfasında kupon kodu alanı olsun.")
            },
            ["backlog"] = new[]
            {
                P("Değerlendirilmiş ama henüz sıraya alınmamış işlerin biriktiği listedir; bir sonraki iş turunda Committed Backlog'a taşınır."),
                E("Kupon kodu alanı: geçersiz kod için hata mesajı gösterilmeli."),
                E("Stok etiketi: 'Son 3 ürün' uyarısı eklenmeli.")
            },
            ["committed-backlog"] = new[]
            {
                P("Bu iş turunda kesin olarak yapılacağı taahhüt edilen kartların listesidir; geliştirmeye başlanınca Dev'e taşınır."),
                E("Bu turda: kupon kodu doğrulama servisi."),
                E("Bu turda: stok etiketi bileşeni.")
            },
            ["dev"] = new[]
            {
                P("Aktif olarak kod yazılan kartların listesidir; geliştirme bitince Dev Done'a taşınır."),
                E("Kupon kodu servisi geliştiriliyor."),
                E("Stok etiketi bileşeni geliştiriliyor.")
            },
            ["dev-done"] = new[]
            {
                P("Kodu tamamlanmış, code review'a hazır kartların listesidir."),
                E("Kupon kodu servisi review'a hazır."),
                E("Stok etiketi bileşeni review'a hazır.")
            },
            ["code-review"] = new[]
            {
                P("Başka bir mühendis tarafından kod incelemesi yapılan kartların listesidir; inceleme bitince Code Review Done'a taşınır."),
                E("Kupon kodu servisinin kod incelemesi sürüyor."),
                E("Stok etiketi bileşeninin kod incelemesi sürüyor.")
            },
            ["code-review-done"] = new[]
            {
                P("Kod incelemesi tamamlanmış, teste hazır kartların listesidir."),
                E("Kupon kodu servisi teste hazır."),
                E("Stok etiketi bileşeni teste hazır.")
            },
            ["testing"] = new[]
            {
                P("Fonksiyonel testlerin yürütüldüğü listedir; testler geçince Testing Done'a taşınır."),
                E("Kupon kodu servisinin test senaryoları çalıştırılıyor."),
                E("Stok etiketinin farklı ürün durumlarında testi yapılıyor.")
            },
            ["testing-done"] = new[]
            {
                P("Testi geçen, canlıya almaya hazır kartların listesidir."),
                E("Kupon kodu servisi canlıya almaya hazır."),
                E("Stok etiketi bileşeni canlıya almaya hazır.")
            },
            ["deployed"] = new[]
            {
                P("Canlı ortama alınmış ama müşteri onayı bekleyen kartların listesidir; müşteri onaylarsa Done'a, sorun bulursa (açıklama girerek) Backlog'a geri döner."),
                E("Kupon kodu servisi canlıda, müşteri onayı bekleniyor."),
                E("Stok etiketi canlıda, müşteri onayı bekleniyor.")
            },
            ["done"] = new[]
            {
                P("Müşteri tarafından onaylanan, tamamen tamamlanmış kartların son durağı."),
                E("Kupon kodu özelliği onaylandı."),
                E("Stok etiketi özelliği onaylandı.")
            }
        },

        [BoardTemplates.SiteReliability] = new()
        {
            ["planning"] = new[]
            {
                P("Müşterinin ilettiği planlı iyileştirme/bakım talepleri burada toplanır; değerlendirilince Next Up'a taşınır."),
                E("Sunucu kapasitesinin gözden geçirilmesi talebi."),
                E("Yedekleme sıklığının artırılması talebi.")
            },
            ["issues-and-requests"] = new[]
            {
                P("Müşterinin bildirdiği aksaklık/sorun bildirimleri burada toplanır; değerlendirilince Next Up'a taşınır."),
                E("Sayfa bazen çok yavaş açılıyor, kontrol edilebilir mi?"),
                E("Dün akşam sisteme birkaç dakika erişemedik.")
            },
            ["next-up"] = new[]
            {
                P("Planlama ve sorun bildirimlerinden gelip sıraya alınan, bir sonraki ele alınacak işlerin listesidir; bir mühendis üstlenince Doing'e taşınır."),
                E("Sırada: sunucu kapasite artışı."),
                E("Sırada: yavaş sayfa açılışının kök neden analizi.")
            },
            ["doing"] = new[]
            {
                P("Aktif olarak üzerinde çalışılan kartların listesidir; iş bitince In Code Review'a taşınır."),
                E("Kapasite artışı için sunucu yapılandırması yapılıyor."),
                E("Yavaş sayfa için performans profili çıkarılıyor.")
            },
            ["in-code-review"] = new[]
            {
                P("Yapılan değişikliğin başka bir mühendis tarafından incelendiği listedir; onay sonrası Staging'e taşınır."),
                E("Kapasite yapılandırma değişikliği inceleniyor."),
                E("Performans düzeltmesi kodu inceleniyor.")
            },
            ["staging"] = new[]
            {
                P("Değişikliğin canlıya benzer bir test ortamında doğrulandığı listedir; sorunsuzsa Prod'a taşınır."),
                E("Kapasite değişikliği staging'de doğrulanıyor."),
                E("Performans düzeltmesi staging'de doğrulanıyor.")
            },
            ["prod"] = new[]
            {
                P("Canlı ortama alınmış, müşteri onayı bekleyen kartların listesidir; müşteri onaylarsa Done'a, sorun bulursa (açıklama girerek) Issues and Requests'e geri döner."),
                E("Kapasite artışı canlıda, onayınız bekleniyor."),
                E("Performans düzeltmesi canlıda, onayınız bekleniyor.")
            },
            ["done"] = new[]
            {
                P("Müşteri tarafından onaylanmış, tamamlanmış kartların son durağı."),
                E("Sunucu kapasite artışı onaylandı."),
                E("Sayfa performans düzeltmesi onaylandı.")
            },
            ["recurring"] = new[]
            {
                P("Belirli aralıklarla tekrar eden, düzenli bakım işlerinin listesidir; diğer listelerden bağımsız çalışır."),
                E("Haftalık yedekleme kontrolü."),
                E("Aylık güvenlik güncellemesi taraması.")
            }
        },

        [BoardTemplates.SoftwareDevelopment] = new()
        {
            ["requests"] = new[]
            {
                P("Müşterinin ilettiği tüm talepler önce burada toplanır; değerlendirilince Backlog'a taşınır."),
                E("Ürün karşılaştırma ekranı eklensin."),
                E("Favori ürünler listesi eklensin.")
            },
            ["backlog"] = new[]
            {
                P("Değerlendirilmiş, henüz bir sprint'e alınmamış işlerin biriktiği listedir; bir sonraki sprint'te Sprint Backlog'a taşınır."),
                E("Karşılaştırma ekranı: en fazla 4 ürün seçilebilmeli."),
                E("Favoriler: giriş yapmayan kullanıcı için tarayıcıda saklanmalı.")
            },
            ["sprint-backlog"] = new[]
            {
                P("Bu sprint'te yapılması planlanan kartların listesidir; çalışmaya başlanınca Working on Bugs'a taşınır."),
                E("Bu sprint: ürün karşılaştırma ekranı."),
                E("Bu sprint: favori ürünler listesi.")
            },
            ["working-on-bugs"] = new[]
            {
                P("Sprint içinde aktif olarak geliştirilen/hata düzeltmesi yapılan kartların listesidir; iş bitince Testing'e taşınır."),
                E("Karşılaştırma ekranı geliştiriliyor."),
                E("Favori ürünler listesi geliştiriliyor.")
            },
            ["testing"] = new[]
            {
                P("Sprint sonunda testleri yapılan kartların listesidir; test geçince Sprint Done'a taşınır."),
                E("Karşılaştırma ekranının testleri sürüyor."),
                E("Favori ürünler listesinin testleri sürüyor.")
            },
            ["sprint-done"] = new[]
            {
                P("Sprint'i tamamlayan kartların, müşteri tarafından tek tek Onaylandı/Reddedildi olarak işaretlendiği listedir; tüm kartlar karara bağlanınca yeni sprint turu başlar."),
                E("Ürün karşılaştırma ekranı onayınızı bekliyor."),
                E("Favori ürünler listesi onayınızı bekliyor.")
            }
        },

        [BoardTemplates.MusteriSeffaflik] = new()
        {
            ["customer-requests"] = new[]
            {
                P("Müşterinin ilettiği tüm talepler önce burada toplanır; bir mühendis değerlendirince Under Review'a taşınır."),
                E("Fatura geçmişini Excel olarak indirebilir miyiz?"),
                E("Bildirimleri e-posta yerine SMS olarak da alabilir miyim?")
            },
            ["under-review"] = new[]
            {
                P("Talebin teknik olarak incelendiği listedir; uygunsa Approved & Planned'a taşınır."),
                E("Excel dışa aktarma talebi inceleniyor."),
                E("SMS bildirim talebi inceleniyor.")
            },
            ["approved-planned"] = new[]
            {
                P("İncelemesi tamamlanıp onaylanan, geliştirme takvimine alınan kartların listesidir; çalışma başlayınca In Development'a taşınır."),
                E("Onaylandı: Excel dışa aktarma, bu ay planlandı."),
                E("Onaylandı: SMS bildirimi, gelecek ay planlandı.")
            },
            ["in-development"] = new[]
            {
                P("Aktif olarak geliştirilen kartların listesidir; geliştirme bitince In Testing'e taşınır."),
                E("Excel dışa aktarma özelliği geliştiriliyor."),
                E("SMS bildirim entegrasyonu geliştiriliyor.")
            },
            ["in-testing"] = new[]
            {
                P("Geliştirmesi biten kartların iç testlerden geçtiği listedir; testler geçince Customer Preview'a taşınır."),
                E("Excel dışa aktarma test ediliyor."),
                E("SMS bildirimi test ediliyor.")
            },
            ["customer-preview"] = new[]
            {
                P("Müşterinin canlıya almadan önce değişikliği görüp onayladığı/reddettiği listedir; onaylarsa Live'a, reddederse (açıklama girerek) Under Review'a geri döner."),
                E("Excel dışa aktarma önizlemeniz hazır, onayınızı bekliyor."),
                E("SMS bildirimi önizlemeniz hazır, onayınızı bekliyor.")
            },
            ["live"] = new[]
            {
                P("Müşteri tarafından onaylanıp canlıya alınan kartların son durağı."),
                E("Excel dışa aktarma özelliği canlıda."),
                E("SMS bildirimi özelliği canlıda.")
            },
            ["rejected-on-hold"] = new[]
            {
                P("Herhangi bir aşamada beklemeye alınan veya reddedilen kartların toplandığı listedir."),
                E("SMS bildirimi maliyet nedeniyle şimdilik beklemede."),
                E("Fatura tasarımı değişikliği ileri bir tarihe ertelendi.")
            }
        },

        [BoardTemplates.HibritGelistirme] = new()
        {
            ["customer-requests"] = new[]
            {
                P("Müşterinin ilettiği tüm talepler önce burada toplanır; bir mühendis değerlendirince Requirement Discussion'a taşınır."),
                E("Sipariş takibi için harita üzerinde konum gösterimi istiyorum."),
                E("Fatura PDF'inde şirket logosu daha büyük olsun.")
            },
            ["requirement-discussion"] = new[]
            {
                P("Talebin mühendisle birlikte netleştirildiği listedir; görüşme tamamlanınca Backlog'a taşınır."),
                E("Harita gösterimi: hangi harita servisi kullanılacak, netleştiriliyor."),
                E("Logo boyutu için tasarım onayı bekleniyor.")
            },
            ["backlog"] = new[]
            {
                P("Netleşmiş, geliştirilmeye hazır kartların biriktiği listedir; bir mühendis üstlenince Dev'e taşınır."),
                E("Hazır: harita üzerinde konum gösterimi."),
                E("Hazır: fatura PDF logo boyutu güncellemesi.")
            },
            ["dev"] = new[]
            {
                P("Aktif olarak kod yazılan kartların listesidir; geliştirme bitince Code Review'a taşınır."),
                E("Harita entegrasyonu geliştiriliyor."),
                E("Logo boyutu güncellemesi geliştiriliyor.")
            },
            ["code-review"] = new[]
            {
                P("Başka bir mühendis tarafından kod incelemesi yapılan kartların listesidir; onay sonrası Testing'e taşınır."),
                E("Harita entegrasyonu kod incelemesinde."),
                E("Logo boyutu değişikliği kod incelemesinde.")
            },
            ["testing"] = new[]
            {
                P("Fonksiyonel testlerin yürütüldüğü listedir; testler geçince Customer Approval'a taşınır."),
                E("Harita entegrasyonu test ediliyor."),
                E("Logo boyutu değişikliği test ediliyor.")
            },
            ["customer-approval"] = new[]
            {
                P("Canlıya almadan önce müşterinin son onayının alındığı listedir; onaylarsa Deployed'a, reddederse (açıklama girerek) Requirement Discussion'a geri döner."),
                E("Harita entegrasyonu onayınızı bekliyor."),
                E("Logo boyutu değişikliği onayınızı bekliyor.")
            },
            ["deployed"] = new[]
            {
                P("Müşteri onayı alınıp canlı ortama alınan, son bir mühendis kontrolü bekleyen kartların listesidir; kontrol tamamlanınca Done'a taşınır."),
                E("Harita entegrasyonu canlıda, son kontrol yapılıyor."),
                E("Logo boyutu değişikliği canlıda, son kontrol yapılıyor.")
            },
            ["done"] = new[]
            {
                P("Tüm adımları tamamlanmış, kalıcı olarak canlıda olan kartların son durağı."),
                E("Harita üzerinde konum gösterimi tamamlandı."),
                E("Fatura PDF logo boyutu güncellemesi tamamlandı.")
            }
        }
    };

    private static readonly PreviewSeedCard[] Fallback =
    {
        new(PurposeTitle, "Bu listeye taşınan kartlar bu aşamadaki işleri temsil eder."),
        new("Örnek talep 1"),
        new("Örnek talep 2")
    };

    public static PreviewSeedCard[] GetCardsFor(string templateKey, string listKey) =>
        Data.TryGetValue(templateKey, out var lists) && lists.TryGetValue(listKey, out var cards)
            ? cards
            : Fallback;
}
