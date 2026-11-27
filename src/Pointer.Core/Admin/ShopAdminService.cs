using Microsoft.EntityFrameworkCore;
using Pointer.Core.Availability;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Admin;

/// <summary>Validation failure with a resource key for the message shown to the owner.</summary>
public sealed record AdminError(string Field, string MessageKey);

public sealed record AdminResult(IReadOnlyList<AdminError> Errors)
{
    public static readonly AdminResult Ok = new([]);
    public bool Succeeded => Errors.Count == 0;
    public static AdminResult Fail(string field, string key) => new([new AdminError(field, key)]);
}

public sealed record ServiceInput(string Name, int DurationMinutes, decimal PriceEuro, decimal DepositEuro, bool IsActive);

public sealed record HoursInput(DayOfWeek Day, TimeOnly? Opens, TimeOnly? Closes, TimeOnly? Opens2, TimeOnly? Closes2);

public sealed record TimeOffInput(DateOnly FromLocal, DateOnly ToLocal, string? Note);

public sealed record SettingsInput(
    string Name,
    string? Address,
    string? OwnerEmail,
    NoShowPolicy NoShowPolicy,
    int FreeCancellationHours,
    int ReminderHoursBefore,
    bool OwnerInstantEmails,
    bool OwnerDailyOverview,
    bool NoShowNoticeEnabled,
    string? PaymentAccountId);

/// <summary>
/// Everything the owner edits in the admin. Every method takes the shop id from the signed-in
/// owner, never from the request, so one shop can never touch another shop's data.
/// </summary>
public sealed class ShopAdminService(PointerDbContext db, TimeProvider clock)
{
    public Task<Shop> GetShopAsync(Guid shopId, CancellationToken ct = default) =>
        db.Shops.AsNoTracking().SingleAsync(s => s.Id == shopId, ct);

    // Services

    public Task<List<Service>> ListServicesAsync(Guid shopId, CancellationToken ct = default) =>
        db.Services.AsNoTracking().Where(s => s.ShopId == shopId).OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync(ct);

    public Task<Service?> GetServiceAsync(Guid shopId, Guid serviceId, CancellationToken ct = default) =>
        db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.ShopId == shopId && s.Id == serviceId, ct);

    /// <summary>Creates a service when <paramref name="serviceId"/> is null, otherwise updates it.</summary>
    public async Task<AdminResult> SaveServiceAsync(Guid shopId, Guid? serviceId, ServiceInput input, CancellationToken ct = default)
    {
        var errors = new List<AdminError>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors.Add(new("Name", "Error_Required"));
        if (input.DurationMinutes is < 5 or > 480 || input.DurationMinutes % 5 != 0) errors.Add(new("DurationMinutes", "Error_Duration"));
        if (input.PriceEuro < 0) errors.Add(new("PriceEuro", "Error_Amount"));
        if (input.DepositEuro < 0 || input.DepositEuro > input.PriceEuro) errors.Add(new("DepositEuro", "Error_Deposit"));
        if (errors.Count > 0) return new AdminResult(errors);

        Service service;
        if (serviceId is null)
        {
            service = new Service { ShopId = shopId, Name = "" };
            db.Services.Add(service);
        }
        else
        {
            service = await db.Services.FirstOrDefaultAsync(s => s.ShopId == shopId && s.Id == serviceId, ct)
                ?? throw new KeyNotFoundException();
        }

        service.Name = input.Name.Trim();
        service.DurationMinutes = input.DurationMinutes;
        service.PriceCents = ToCents(input.PriceEuro);
        service.DepositCents = ToCents(input.DepositEuro);
        service.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok;
    }

    // Opening hours (shop-wide; per-staff hours are out of scope for v1)

    public async Task<List<HoursInput>> GetWeekAsync(Guid shopId, CancellationToken ct = default)
    {
        var rows = await db.OpeningHours.AsNoTracking().Where(h => h.ShopId == shopId && h.StaffId == null).ToListAsync(ct);
        return WeekOrder.Select(day =>
        {
            var r = rows.Where(h => h.DayOfWeek == day).OrderBy(h => h.Opens).ToList();
            return new HoursInput(day,
                r.ElementAtOrDefault(0)?.Opens, r.ElementAtOrDefault(0)?.Closes,
                r.ElementAtOrDefault(1)?.Opens, r.ElementAtOrDefault(1)?.Closes);
        }).ToList();
    }

    /// <summary>Replaces the weekly hours. A day has up to two intervals (for a lunch break); empty means closed.</summary>
    public async Task<AdminResult> SaveWeekAsync(Guid shopId, IReadOnlyList<HoursInput> week, CancellationToken ct = default)
    {
        var errors = new List<AdminError>();
        var rows = new List<OpeningHours>();
        foreach (var d in week)
        {
            var intervals = new[] { (d.Opens, d.Closes), (d.Opens2, d.Closes2) }
                .Where(i => i.Item1 is not null || i.Item2 is not null)
                .ToList();
            foreach (var (opens, closes) in intervals)
            {
                if (opens is null || closes is null || closes <= opens)
                {
                    errors.Add(new(d.Day.ToString(), "Error_Hours"));
                    continue;
                }
                rows.Add(new OpeningHours { ShopId = shopId, DayOfWeek = d.Day, Opens = opens.Value, Closes = closes.Value });
            }
            if (intervals.Count == 2 && intervals[0].Item2 is { } end1 && intervals[1].Item1 is { } start2 && start2 < end1)
                errors.Add(new(d.Day.ToString(), "Error_HoursOverlap"));
        }
        if (errors.Count > 0) return new AdminResult(errors);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.OpeningHours.Where(h => h.ShopId == shopId && h.StaffId == null).ExecuteDeleteAsync(ct);
        db.OpeningHours.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AdminResult.Ok;
    }

    // Days off

    public Task<List<TimeOff>> ListUpcomingTimeOffAsync(Guid shopId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.TimeOff.AsNoTracking().Where(t => t.ShopId == shopId && t.EndUtc > now).OrderBy(t => t.StartUtc).ToListAsync(ct);
    }

    /// <summary>Closes whole local days, from the start of <c>FromLocal</c> to the end of <c>ToLocal</c>.</summary>
    public async Task<AdminResult> AddTimeOffAsync(Guid shopId, TimeOffInput input, CancellationToken ct = default)
    {
        if (input.ToLocal < input.FromLocal)
            return AdminResult.Fail("ToLocal", "Error_DateRange");

        var shop = await GetShopAsync(shopId, ct);
        var zone = ShopTime.FindZone(shop.TimeZoneId);
        db.TimeOff.Add(new TimeOff
        {
            ShopId = shopId,
            StartUtc = ShopTime.ToUtc(input.FromLocal, TimeOnly.MinValue, zone),
            EndUtc = ShopTime.ToUtc(input.ToLocal.AddDays(1), TimeOnly.MinValue, zone),
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
        });
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok;
    }

    public Task DeleteTimeOffAsync(Guid shopId, Guid timeOffId, CancellationToken ct = default) =>
        db.TimeOff.Where(t => t.ShopId == shopId && t.Id == timeOffId).ExecuteDeleteAsync(ct);

    // Settings

    public async Task<AdminResult> SaveSettingsAsync(Guid shopId, SettingsInput input, CancellationToken ct = default)
    {
        var errors = new List<AdminError>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors.Add(new("Name", "Error_Required"));
        if (input.FreeCancellationHours is < 0 or > 14 * 24) errors.Add(new("FreeCancellationHours", "Error_CancellationHours"));
        if (input.ReminderHoursBefore is < 2 or > 48) errors.Add(new("ReminderHoursBefore", "Error_ReminderHours"));
        if (!string.IsNullOrWhiteSpace(input.OwnerEmail) && !input.OwnerEmail.Contains('@')) errors.Add(new("OwnerEmail", "Error_Email"));
        if (!string.IsNullOrWhiteSpace(input.PaymentAccountId) && !input.PaymentAccountId.Trim().StartsWith("acct_", StringComparison.Ordinal))
            errors.Add(new("PaymentAccountId", "Error_StripeAccount"));
        if (errors.Count > 0) return new AdminResult(errors);

        var shop = await db.Shops.SingleAsync(s => s.Id == shopId, ct);
        shop.Name = input.Name.Trim();
        shop.Address = Clean(input.Address);
        shop.OwnerEmail = Clean(input.OwnerEmail)?.ToLowerInvariant();
        shop.NoShowPolicy = input.NoShowPolicy;
        shop.FreeCancellationMinutes = input.FreeCancellationHours * 60;
        shop.ReminderHoursBefore = input.ReminderHoursBefore;
        shop.OwnerInstantEmails = input.OwnerInstantEmails;
        shop.OwnerDailyOverview = input.OwnerDailyOverview;
        shop.NoShowNoticeEnabled = input.NoShowNoticeEnabled;
        shop.PaymentAccountId = Clean(input.PaymentAccountId);
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok;
    }

    public static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static long ToCents(decimal euro) => (long)Math.Round(euro * 100m, MidpointRounding.AwayFromZero);
}
