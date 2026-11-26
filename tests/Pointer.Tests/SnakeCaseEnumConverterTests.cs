using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Tests;

public sealed class SnakeCaseEnumConverterTests
{
    [Theory]
    [InlineData(BookingStatus.PendingPayment, "pending_payment")]
    [InlineData(BookingStatus.Confirmed, "confirmed")]
    [InlineData(BookingStatus.NoShow, "no_show")]
    public void Round_trips_booking_status(BookingStatus status, string stored)
    {
        Assert.Equal(stored, SnakeCaseEnumConverter<BookingStatus>.ToSnakeCase(status.ToString()));
        Assert.Equal(status, SnakeCaseEnumConverter<BookingStatus>.FromSnakeCase(stored));
    }

    [Fact]
    public void Every_enum_value_round_trips()
    {
        foreach (var v in Enum.GetValues<BookingStatus>())
            Assert.Equal(v, SnakeCaseEnumConverter<BookingStatus>.FromSnakeCase(SnakeCaseEnumConverter<BookingStatus>.ToSnakeCase(v.ToString())));
        foreach (var v in Enum.GetValues<PaymentStatus>())
            Assert.Equal(v, SnakeCaseEnumConverter<PaymentStatus>.FromSnakeCase(SnakeCaseEnumConverter<PaymentStatus>.ToSnakeCase(v.ToString())));
        foreach (var v in Enum.GetValues<NoShowPolicy>())
            Assert.Equal(v, SnakeCaseEnumConverter<NoShowPolicy>.FromSnakeCase(SnakeCaseEnumConverter<NoShowPolicy>.ToSnakeCase(v.ToString())));
    }
}
