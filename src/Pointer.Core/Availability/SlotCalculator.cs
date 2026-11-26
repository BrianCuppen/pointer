namespace Pointer.Core.Availability;

/// <summary>A local weekly open interval, already resolved for one staff member.</summary>
public sealed record OpenInterval(DayOfWeek DayOfWeek, TimeOnly Opens, TimeOnly Closes);

/// <summary>A UTC period that cannot be booked: time off or an existing booking.</summary>
public sealed record BusyPeriod(DateTime StartUtc, DateTime EndUtc);

public sealed record AvailableSlot(DateTime StartUtc, DateTime EndUtc, DateTimeOffset StartLocal);

public sealed record SlotQuery(
    TimeZoneInfo Zone,
    IReadOnlyList<OpenInterval> OpeningHours,
    IReadOnlyList<BusyPeriod> Busy,
    int DurationMinutes,
    DateOnly FromLocal,
    DateOnly ToLocal,
    DateTime NowUtc,
    AvailabilityOptions Options);

/// <summary>
/// Pure slot calculation, no database. Opening hours are local wall-clock times and are converted
/// per calendar day, so 09:00 stays 09:00 local on both sides of a DST change.
/// </summary>
public static class SlotCalculator
{
    public static IReadOnlyList<AvailableSlot> Compute(SlotQuery q)
    {
        var duration = TimeSpan.FromMinutes(q.DurationMinutes);
        var step = TimeSpan.FromMinutes(q.Options.SlotStepMinutes);
        var earliest = q.NowUtc.AddMinutes(q.Options.MinimumNoticeMinutes);
        var latestDay = ShopTime.LocalDate(q.NowUtc, q.Zone).AddDays(q.Options.BookingHorizonDays);
        var to = q.ToLocal > latestDay ? latestDay : q.ToLocal;

        var busy = q.Busy.OrderBy(b => b.StartUtc).ToList();
        var result = new List<AvailableSlot>();

        for (var day = q.FromLocal; day <= to; day = day.AddDays(1))
        {
            foreach (var (openUtc, closeUtc) in OpenWindowsUtc(day, q.Zone, q.OpeningHours))
            {
                for (var start = openUtc; start + duration <= closeUtc; start += step)
                {
                    var end = start + duration;
                    if (start < earliest || Overlaps(busy, start, end))
                        continue;
                    result.Add(new AvailableSlot(start, end, ShopTime.ToLocal(start, q.Zone)));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// True if [startUtc, startUtc + duration) lies inside opening hours, outside the busy periods given,
    /// and within the notice and horizon limits. Not aligned to the step grid: the grid is only for display.
    /// </summary>
    public static bool IsBookable(SlotQuery q, DateTime startUtc)
    {
        var end = startUtc.AddMinutes(q.DurationMinutes);
        if (startUtc < q.NowUtc.AddMinutes(q.Options.MinimumNoticeMinutes))
            return false;

        var day = ShopTime.LocalDate(startUtc, q.Zone);
        if (day > ShopTime.LocalDate(q.NowUtc, q.Zone).AddDays(q.Options.BookingHorizonDays))
            return false;

        var fits = OpenWindowsUtc(day, q.Zone, q.OpeningHours).Any(w => startUtc >= w.OpenUtc && end <= w.CloseUtc);
        return fits && !Overlaps(q.Busy, startUtc, end);
    }

    private static IEnumerable<(DateTime OpenUtc, DateTime CloseUtc)> OpenWindowsUtc(
        DateOnly day, TimeZoneInfo zone, IReadOnlyList<OpenInterval> hours)
    {
        foreach (var h in hours.Where(h => h.DayOfWeek == day.DayOfWeek).OrderBy(h => h.Opens))
        {
            var open = ShopTime.ToUtc(day, h.Opens, zone);
            var close = ShopTime.ToUtc(day, h.Closes, zone);
            if (close > open)
                yield return (open, close);
        }
    }

    private static bool Overlaps(IEnumerable<BusyPeriod> busy, DateTime start, DateTime end) =>
        busy.Any(b => b.StartUtc < end && b.EndUtc > start);
}
