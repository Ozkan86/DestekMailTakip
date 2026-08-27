using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using task_list.Data;
using task_list.Models;

namespace task_list.Services;

/// <summary>
/// Renk indekslerini veritabanindan okuyup onbellekte tutar. Yeni bir kullanici
/// eklendiginde o an kullanilmayan en kucuk indeks verilir; boylece paletteki
/// renkler sirayla tuketilir ve ilk 8 kullanici farkli renk alir
/// (bkz. AvatarPalette).
/// </summary>
public sealed class UserAvatarColorService : IUserAvatarColorService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, int> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _assignLock = new(1, 1);
    private volatile bool _loaded;

    public UserAvatarColorService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public int ColorIndexFor(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return -1;
        }

        EnsureLoaded();

        if (_cache.TryGetValue(userId, out var index) && index >= 0)
        {
            return index;
        }

        // Onbellekte yok (yeni eklenmis kullanici) veya indeksi hic atanmamis:
        // veritabanindan tazeleyip gerekirse indeksi simdi atiyoruz.
        Reload();
        if (_cache.TryGetValue(userId, out index) && index >= 0)
        {
            return index;
        }

        return AssignForUserIdAsync(userId).GetAwaiter().GetResult();
    }

    public string ColorFor(string? userId, string? fallbackSeed = null)
    {
        var index = ColorIndexFor(userId);
        return index >= 0
            ? AvatarPalette.ColorFor(index)
            : AvatarPalette.ColorForSeed(fallbackSeed ?? userId);
    }

    public async Task<int> AssignColorIndexAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        await _assignLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tracked = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
            if (tracked is null)
            {
                return -1;
            }

            if (tracked.AvatarColorIndex < 0)
            {
                tracked.AvatarColorIndex = NextFreeIndex(await UsedIndexesAsync(db, cancellationToken));
                await db.SaveChangesAsync(cancellationToken);
            }

            user.AvatarColorIndex = tracked.AvatarColorIndex;
            _cache[tracked.Id] = tracked.AvatarColorIndex;
            return tracked.AvatarColorIndex;
        }
        finally
        {
            _assignLock.Release();
        }
    }

    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        await _assignLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var all = await db.Users
                .OrderBy(u => u.DisplayName)
                .ThenBy(u => u.Id)
                .ToListAsync(cancellationToken);

            var used = new HashSet<int>(all.Where(u => u.AvatarColorIndex >= 0).Select(u => u.AvatarColorIndex));

            // Ayni indeks birden fazla kullanicidaysa (elle mudahale/eski veri)
            // ilki kalir, digerleri yeniden atanir.
            var seen = new HashSet<int>();
            var changed = false;

            foreach (var user in all)
            {
                if (user.AvatarColorIndex >= 0 && seen.Add(user.AvatarColorIndex))
                {
                    continue;
                }

                var next = NextFreeIndex(used);
                user.AvatarColorIndex = next;
                used.Add(next);
                seen.Add(next);
                changed = true;
            }

            if (changed)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            _cache.Clear();
            foreach (var user in all)
            {
                _cache[user.Id] = user.AvatarColorIndex;
            }

            _loaded = true;
        }
        finally
        {
            _assignLock.Release();
        }
    }

    private async Task<int> AssignForUserIdAsync(string userId)
    {
        await _assignLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
            {
                return -1;
            }

            if (user.AvatarColorIndex < 0)
            {
                user.AvatarColorIndex = NextFreeIndex(await UsedIndexesAsync(db));
                await db.SaveChangesAsync();
            }

            _cache[user.Id] = user.AvatarColorIndex;
            return user.AvatarColorIndex;
        }
        finally
        {
            _assignLock.Release();
        }
    }

    private static async Task<HashSet<int>> UsedIndexesAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        var indexes = await db.Users
            .Where(u => u.AvatarColorIndex >= 0)
            .Select(u => u.AvatarColorIndex)
            .ToListAsync(cancellationToken);
        return new HashSet<int>(indexes);
    }

    private static int NextFreeIndex(HashSet<int> used)
    {
        var candidate = 0;
        while (used.Contains(candidate))
        {
            candidate++;
        }
        return candidate;
    }

    private void EnsureLoaded()
    {
        if (!_loaded)
        {
            Reload();
        }
    }

    private void Reload()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.AvatarColorIndex })
            .ToList();

        foreach (var row in rows)
        {
            _cache[row.Id] = row.AvatarColorIndex;
        }

        _loaded = true;
    }
}
