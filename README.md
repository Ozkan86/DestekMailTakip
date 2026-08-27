# Creamobile Destek — Mail Takip & Görev Panosu

Destek ekiplerinin gelen kutusunu, görev panolarını (Kanban) ve performans istatistiklerini tek bir yerden yönetmesi için geliştirilmiş bir ASP.NET Core MVC uygulaması. IMAP üzerinden mail kutusunu izler, gelen destek taleplerini panolara/kartlara dönüştürüp çalışanlara atar, gönderilen yanıtları SMTP ile yollar ve tüm süreç için istatistik/raporlama sağlar.

## Özellikler

### 📧 Mail Takip (`MailController`)
- IMAP üzerinden gelen kutusunun arka planda otomatik senkronizasyonu (`MailSyncBackgroundService`, `ImapMailService`).
- Mailleri çalışanlara atama, okundu/silindi işaretleme, "yeni mail geldi mi?" için hafif yoklama (polling) uç noktası.
- Mail detay görünümü, yanıt gönderme (SMTP, `MailSenderService`), yanıt taslak şablonları ve ek dosya desteği.
- Mail bayrak/öncelik politikaları (`MailFlagPolicy`) ve silinmiş mailler için ayrı görünüm (`Deleted.cshtml`).

### 🗂️ Görev Panoları (`BoardController`)
- Trello benzeri Kanban panoları: sürükle-bırak kartlar, liste/kolonlar, kart detay modalı.
- Kart özellikleri: zengin metin açıklama/yorumlar (HTML sanitize edilerek güvenli hale getirilir), etiketler, kapak görselleri, ek dosyalar, atanan kişiler.
- Pano şablonları (`BoardTemplateModels`, önizleme tohum verisi) ile hızlı pano oluşturma.
- Pano yetkilendirme paneli: panoya erişebilecek e-posta/kullanıcıların yönetimi.
- Arşivlenmiş panolar ve "Görevlerim" (bana atanan kartlar) görünümü.
- Kart/pano olayları için bildirim kuyruğu ve arka plan servisi (`BoardNotificationQueue`, `BoardNotificationBackgroundService`).

### 📊 İstatistikler (`StatsController`)
- Mühendis profil menüsünden açılan "İstatistiklerim" sayfası.
- Mail (destek) ve pano istatistiklerini birleştirik gösterir; kullanıcıya özel notlar eklenebilir.
- "Decay'li" (zamanla etkisi azalan) istatistik olayları: bir olay görüldükten 4 saat sonra sayaçları etkilemeyi bırakır.
- Kart olayları ve mesaj geçmişi için ayrıntı çekmeceleri (drawer).

### 👤 Kullanıcı & Yetkilendirme
- ASP.NET Core Identity tabanlı giriş sistemi (`AccountController`), roller: **Admin**, **Employee**, **Customer**.
- Kayıt ekranı kapalıdır; hesaplar yalnızca ilk açılışta tohumlanan yönetici hesabı veya Admin panelinden oluşturulabilir.
- Admin panelinden çalışan (Employee) hesaplarının yönetimi (`AdminController` → `Views/Admin/Employees.cshtml`).
- Kullanıcı bazlı avatar renk ataması (`UserAvatarColorService`).
- Uygulama içi bildirimler (`NotificationsController`).

## Teknoloji Yığını

- **.NET 8** / ASP.NET Core MVC
- **Entity Framework Core 8** + **SQL Server** (`Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Data.SqlClient`)
- **ASP.NET Core Identity** (kullanıcı/rol yönetimi)
- **MailKit** (IMAP okuma / SMTP gönderme)
- **HtmlSanitizer** (zengin metin içeriklerini güvenli hale getirme)
- Sunucu taraflı Razor View'lar + vanilla JS/CSS (`wwwroot`)

## Proje Yapısı

```
Controllers/    MVC controller'ları (Mail, Board, Stats, Admin, Account, Notifications, Home)
Data/           EF Core DbContext, repository sınıfları ve arayüzleri, migration'lar
Models/         Domain modelleri ve view model'ler
Services/       IMAP/SMTP servisleri, arka plan işleri, bildirim kuyruğu, avatar renk servisi
Views/          Razor görünümleri (controller başına klasör)
Sql/            Tam veritabanı kurulum script'i (FullDatabaseSetup.sql)
wwwroot/        Statik dosyalar (css, js, assets, uploads)
```

## Kurulum

### Gereksinimler
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (LocalDB, Express veya tam sürüm)
- (Opsiyonel) IMAP/SMTP erişimi olan bir mail hesabı — mail senkronizasyonu ve gönderimi için

### 1. Depoyu klonlayın
```bash
git clone <repo-url>
cd task_list
```

### 2. Ayarları yapılandırın
`appsettings.Example.json` dosyasını referans alarak `appsettings.json` içindeki bağlantı dizesini ve IMAP/SMTP bilgilerini kendi ortamınıza göre düzenleyin. `appsettings.Development.json` ve `appsettings.*.local.json` dosyaları `.gitignore` ile takip dışıdır; yerel/geliştirme ortamına özel gizli bilgileri (örn. gerçek mail şifreleri) bu dosyalara koymanız önerilir.

Doldurmanız gereken alanlar:

| Anahtar | Açıklama |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server bağlantı dizesi |
| `ImapSettings:Host/Port/UseSsl` | Gelen kutusunun IMAP sunucu bilgileri |
| `ImapSettings:Username/Password` | IMAP hesap bilgileri (Gmail için uygulama şifresi önerilir) |
| `ImapSettings:PollIntervalSeconds` | Gelen kutusunun kaç saniyede bir kontrol edileceği |
| `ImapSettings:MailboxName` | İzlenecek klasör (varsayılan `INBOX`) |
| `SmtpSettings:Host/Port/UseSsl` | Giden mail (SMTP) sunucu bilgileri |
| `SmtpSettings:Username/Password` | SMTP hesap bilgileri |
| `SmtpSettings:FromDisplayName` | Giden maillerde görünecek gönderici adı |

### 3. Veritabanını oluşturun
İki yoldan biriyle veritabanını hazırlayabilirsiniz:

**A) EF Core migration'ları ile:**
```bash
dotnet tool install --global dotnet-ef   # kurulu değilse
dotnet ef database update
```

**B) Hazır SQL script'i ile:**
`Sql/FullDatabaseSetup.sql` dosyasını SQL Server Management Studio / Azure Data Studio / `sqlcmd` ile hedef sunucunuza çalıştırın. Script idempotenttir, tekrar tekrar çalıştırılabilir.

### 4. Uygulamayı çalıştırın
```bash
dotnet run
```

Uygulama ilk açılışta `Admin`, `Employee`, `Customer` rollerini ve varsayılan bir yönetici hesabı tohumlar (kullanıcı adı: `creamobile_yonetici`). Giriş yaptıktan sonra Admin panelinden diğer çalışan hesaplarını oluşturabilirsiniz.

> Kayıt (register) ekranı üretimde kapalıdır; yeni hesaplar yalnızca Admin panelinden açılır.

## Kullanım Akışı

1. **Giriş yapın** — `/Account/Login` üzerinden yönetici veya çalışan hesabınızla giriş yapın.
2. **Mail takibi** — `/Mail` altında gelen destek maillerini görüntüleyin, kendinize veya bir çalışana atayın, yanıtlayın.
3. **Pano oluşturun** — `/Board` altında yeni bir pano (şablondan veya sıfırdan) oluşturup kartlar ekleyin, etiketleyin, yorum yapın, sürükleyip taşıyın.
4. **Görevlerinizi takip edin** — "Görevlerim" sayfasından size atanan tüm kartları tek yerden görün.
5. **İstatistiklerinizi inceleyin** — Profil menüsündeki rozetten "İstatistiklerim" sayfasını açarak mail ve pano performans verilerinizi görün.
6. **Yönetim** — Admin rolündeki kullanıcılar `/Admin/Employees` üzerinden çalışan hesaplarını yönetir.

## Güvenlik Notları

- Tüm zengin metin (kart açıklaması, yorumlar) sunucuya kaydedilmeden önce `HtmlSanitizer` ile temizlenir; script/`on*` olay özniteliği/`javascript:` enjeksiyonlarına karşı korunur.
- `appsettings.Development.json` ve `appsettings.*.local.json` `.gitignore` içindedir; gerçek mail şifreleri gibi ortam bazlı gizli bilgileri bu dosyalarda tutup versiyon kontrolüne dahil etmeyin.
- Mail ve pano işlemleri `[Authorize]` ile korunur; roller bazında ek kısıtlamalar uygulanır (ör. `MailController` yalnızca `Admin,Employee`).
