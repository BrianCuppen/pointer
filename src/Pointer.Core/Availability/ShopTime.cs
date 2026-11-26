namespace Pointer.Core.Availability;

/// <summary>Conversions between a shop's local wall-clock time and UTC, with explicit DST rules.</summary>
public static class ShopTime
{
    public static TimeZoneInfo FindZone(string ianaId) => TimeZoneInfo.FindSystemTimeZoneById(ianaId);

    /// <summary>
    /// Converts a local wall-clock time to UTC.
    /// A time inside the spring-forward gap (for example 02:30 on the last Sunday of March in Brussels)
    /// does not exist and moves forward to the end of the gap (03:00).
    /// A time inside the fall-back overlap (02:30 on the last Sunday of October) happens twice;
    /// the first occurrence (still summer time) is used.
    /// </summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            var shifted = local;
            while (zone.IsInvalidTime(shifted))
                shifted = shifted.AddMinutes(1);
            // Snap to the first valid wall-clock minute after the gap, e.g. 03:00.
            local = shifted;
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateTimeOffset ToLocal(DateTime utc, TimeZoneInfo zone)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Expected a UTC DateTime.", nameof(utc));
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), zone);
    }

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(utc, zone).DateTime);
}
