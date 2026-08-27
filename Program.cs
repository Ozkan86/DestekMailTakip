using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using task_list.Data;
using task_list.Models;
using task_list.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection connection string is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 6;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.Configure<ImapSettings>(builder.Configuration.GetSection("ImapSettings"));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddScoped<IMailRepository, MailRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IImapMailService, ImapMailService>();
builder.Services.AddScoped<IMailSenderService, MailSenderService>();
builder.Services.AddScoped<IBoardRepository, BoardRepository>();
builder.Services.AddScoped<IStatsRepository, StatsRepository>();
builder.Services.AddSingleton<IUserAvatarColorService, UserAvatarColorService>();
builder.Services.AddSingleton<IBoardNotificationQueue, BoardNotificationQueue>();
builder.Services.AddSingleton<IMailSyncCoordinator, MailSyncCoordinator>();
builder.Services.AddHostedService<MailSyncBackgroundService>();
builder.Services.AddHostedService<BoardNotificationBackgroundService>();

var app = builder.Build();

// Rol altyapisi ve yonetici hesabi: idempotent, her baslangicta guvenle calisir.
// Kayit ekrani kaldirildigi icin hesaplar artik sadece bu tohumlama (admin) ve
// Admin panelinden (calisanlar) olusturulabiliyor.
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    foreach (var role in new[] { "Admin", "Employee", "Customer" })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    const string adminUserName = "creamobile_yonetici";
    var adminUser = await userManager.FindByNameAsync(adminUserName);
    if (adminUser is null)
    {
        adminUser = new ApplicationUser { UserName = adminUserName, DisplayName = "Yönetici" };
        var createResult = await userManager.CreateAsync(adminUser, "creamobile_yonetici");
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
        else
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError("Yönetici hesabı oluşturulamadı: {Errors}",
                string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }
    }
    else if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
    {
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }

    // Rozet renkleri: indeksi olmayan (veya cakisan) kullanicilara sirayla indeks ata.
    var avatarColors = app.Services.GetRequiredService<IUserAvatarColorService>();
    await avatarColors.BackfillAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Mail}/{action=Index}/{id?}");

app.Run();
