using Pointer.Core.Availability;

namespace Pointer.Tests;

public sealed class SlotCalculatorTests
{
    private static readonly TimeZoneInfo Brussels = ShopTime.FindZone("Europe/Brussels");
    private static readonly AvailabilityOptions Defaults = new() { SlotStepMinutes = 15, MinimumNoticeMinutes = 60, BookingHorizonDays = 60 };

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static OpenInterval[] EveryDay(TimeOnly opens, TimeOnly closes) =>
        Enum.GetValues<DayOfWeek>().Select(d => new OpenInterval(d, opens, closes)).ToArray();

    private static SlotQuery Query(DateOnly day, IReadOnlyList<OpenInterval> hours, DateTime now,
        IReadOnlyList<BusyPeriod>? busy = null, int duration = 30, AvailabilityOptions? options = null) =>
        new(Brussels, hours, busy ?? [], duration, day, day, now, options ?? Defaults);

    [Fact]
    public void Generates_slots_in_steps_that_fit_inside_opening_hours()
    {
        var day = new DateOnly(2026, 12, 2); // winter: UTC+1
        var slots = SlotCalculator.Compute(Query(day, EveryDay(new(9, 0), new(10, 0)), Utc(2026, 12, 1, 8)));

        Assert.Equal([Utc(2026, 12, 2, 8, 0), Utc(2026, 12, 2, 8, 15), Utc(2026, 12, 2, 8, 30)], slots.Select(s => s.StartUtc));
        Assert.Equal(new DateTimeOffset(2026, 12, 2, 9, 0, 0, TimeSpan.FromHours(1)), slots[0].StartLocal);
    }

    [Fact]
    public void Lunch_break_splits_the_day()
    {
        var day = new DateOnly(2026, 12, 2);
        var hours = new[] { new OpenInterval(DayOfWeek.Wednesday, new(9, 0), new(10, 0)), new OpenInterval(DayOfWeek.Wednesday, new(13, 0), new(13, 30)) };

        var slots = SlotCalculator.Compute(Query(day, hours, Utc(2026, 12, 1, 8), duration: 60));

        Assert.Equal([Utc(2026, 12, 2, 8, 0)], slots.Select(s => s.StartUtc));
    }

    [Fact]
    public void Busy_periods_remove_overlapping_slots_but_not_adjacent_ones()
    {
        var day = new DateOnly(2026, 12, 2);
        var busy = new[] { new BusyPeriod(Utc(2026, 12, 2, 8, 30), Utc(2026, 12, 2, 9, 0)) };

        var slots = SlotCalculator.Compute(Query(day, EveryDay(new(9, 0), new(11, 0)), Utc(2026, 12, 1, 8), busy));

        var starts = slots.Select(s => s.StartUtc).ToList();
        Assert.Contains(Utc(2026, 12, 2, 8, 0), starts);   // 09:00-09:30 local ends exactly when busy starts
        Assert.DoesNotContain(Utc(2026, 12, 2, 8, 15), starts);
        Assert.DoesNotContain(Utc(2026, 12, 2, 8, 45), starts);
        Assert.Contains(Utc(2026, 12, 2, 9, 0), starts);   // starts exactly when busy ends
    }

    [Fact]
    public void Respects_minimum_notice()
    {
        var day = new DateOnly(2026, 12, 2);
        var now = Utc(2026, 12, 2, 8, 10); // 09:10 local; notice 60 min -> earliest 10:10 local

        var slots = SlotCalculator.Compute(Query(day, EveryDay(new(9, 0), new(11, 0)), now));

        Assert.Equal(Utc(2026, 12, 2, 9, 15), slots.First().StartUtc);
    }

    [Fact]
    public void Respects_booking_horizon()
    {
        var now = Utc(2026, 12, 1, 8);
        var q = new SlotQuery(Brussels, EveryDay(new(9, 0), new(10, 0)), [], 30,
            new DateOnly(2026, 12, 1), new DateOnly(2027, 6, 1), now, new AvailabilityOptions { BookingHorizonDays = 3 });

        var lastDay = SlotCalculator.Compute(q).Max(s => DateOnly.FromDateTime(s.StartLocal.DateTime));

        Assert.Equal(new DateOnly(2026, 12, 4), lastDay);
    }

    [Fact]
    public void Opening_time_stays_local_across_spring_dst_change()
    {
        // Brussels switches to summer time on Sunday 28 March 2027, 02:00 -> 03:00.
        var hours = EveryDay(new(9, 0), new(9, 30));
        var now = Utc(2027, 3, 20, 8);

        var saturday = SlotCalculator.Compute(Query(new DateOnly(2027, 3, 27), hours, now)).Single();
        var sunday = SlotCalculator.Compute(Query(new DateOnly(2027, 3, 28), hours, now)).Single();

        Assert.Equal(Utc(2027, 3, 27, 8, 0), saturday.StartUtc); // UTC+1
        Assert.Equal(Utc(2027, 3, 28, 7, 0), sunday.StartUtc);   // UTC+2
        Assert.Equal(new TimeSpan(9, 0, 0), saturday.StartLocal.TimeOfDay);
        Assert.Equal(new TimeSpan(9, 0, 0), sunday.StartLocal.TimeOfDay);
    }

    [Fact]
    public void Opening_time_stays_local_across_autumn_dst_change()
    {
        // Brussels switches back to winter time on Sunday 31 October 2027, 03:00 -> 02:00.
        var hours = EveryDay(new(9, 0), new(9, 30));
        var now = Utc(2027, 10, 20, 8);

        var saturday = SlotCalculator.Compute(Query(new DateOnly(2027, 10, 30), hours, now)).Single();
        var sunday = SlotCalculator.Compute(Query(new DateOnly(2027, 10, 31), hours, now)).Single();

        Assert.Equal(Utc(2027, 10, 30, 7, 0), saturday.StartUtc); // UTC+2
        Assert.Equal(Utc(2027, 10, 31, 8, 0), sunday.StartUtc);   // UTC+1
    }

    [Fact]
    public void Interval_across_spring_gap_is_one_hour_shorter()
    {
        // Open 01:00-04:00 local on the spring-forward night: only 2 real hours exist.
        var hours = new[] { new OpenInterval(DayOfWeek.Sunday, new(1, 0), new(4, 0)) };

        var slots = SlotCalculator.Compute(Query(new DateOnly(2027, 3, 28), hours, Utc(2027, 3, 20, 8), duration: 60,
            options: new AvailabilityOptions { SlotStepMinutes = 60 }));

        Assert.Equal([Utc(2027, 3, 28, 0, 0), Utc(2027, 3, 28, 1, 0)], slots.Select(s => s.StartUtc));
    }

    [Fact]
    public void Nonexistent_local_time_moves_to_end_of_gap()
    {
        Assert.Equal(Utc(2027, 3, 28, 1, 0), ShopTime.ToUtc(new DateOnly(2027, 3, 28), new TimeOnly(2, 30), Brussels));
    }

    [Fact]
    public void Ambiguous_local_time_uses_first_occurrence()
    {
        // 02:30 happens twice on 31 Oct 2027: first at UTC+2 (00:30Z), then at UTC+1 (01:30Z).
        Assert.Equal(Utc(2027, 10, 31, 0, 30), ShopTime.ToUtc(new DateOnly(2027, 10, 31), new TimeOnly(2, 30), Brussels));
    }

    [Fact]
    public void IsBookable_checks_hours_notice_and_busy_but_not_the_step_grid()
    {
        var day = new DateOnly(2026, 12, 2);
        var busy = new[] { new BusyPeriod(Utc(2026, 12, 2, 10, 0), Utc(2026, 12, 2, 11, 0)) };
        var q = Query(day, EveryDay(new(9, 0), new(18, 0)), Utc(2026, 12, 1, 8), busy);

        Assert.True(SlotCalculator.IsBookable(q, Utc(2026, 12, 2, 8, 7)));   // off-grid but inside hours
        Assert.False(SlotCalculator.IsBookable(q, Utc(2026, 12, 2, 7, 45))); // 08:45 local, before opening
        Assert.False(SlotCalculator.IsBookable(q, Utc(2026, 12, 2, 16, 45))); // ends 18:15 local
        Assert.False(SlotCalculator.IsBookable(q, Utc(2026, 12, 2, 10, 15))); // busy
        Assert.False(SlotCalculator.IsBookable(q, Utc(2026, 12, 1, 8, 30))); // within notice
    }
}
