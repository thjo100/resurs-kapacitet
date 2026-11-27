using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;

namespace ResursKapacitet.Api.Services;

/// <summary>Cachar inställningarna i minnet. Invalideras när admin sparar.</summary>
public class SettingsStore(IServiceScopeFactory scopeFactory)
{
    private AppSettings? _cached;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cached is { } s) return s;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is { } s2) return s2;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            _cached = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AppSettings();
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _cached = null;
}
