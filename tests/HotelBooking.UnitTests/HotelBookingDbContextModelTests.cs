using HotelBooking.Models;
using HotelBooking.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.UnitTests;

public sealed class HotelBookingDbContextModelTests
{
    [Fact]
    public void Booking_reference_has_unique_index()
    {
        using var context = CreateContext();

        var bookingEntity = context.Model.FindEntityType(typeof(Booking));

        Assert.NotNull(bookingEntity);

        var bookingReferenceIndex = Assert.Single(
            bookingEntity.GetIndexes(),
            index => index.Properties.Any(property => property.Name == nameof(Booking.BookingReference)));

        Assert.True(bookingReferenceIndex.IsUnique);
    }

    [Fact]
    public void Room_type_is_stored_as_readable_string()
    {
        using var context = CreateContext();

        var roomEntity = context.Model.FindEntityType(typeof(Room));
        var roomTypeProperty = roomEntity?.FindProperty(nameof(Room.RoomType));

        Assert.NotNull(roomTypeProperty);
        Assert.Equal(typeof(string), roomTypeProperty.GetProviderClrType());
        Assert.Equal(20, roomTypeProperty.GetMaxLength());
    }

    [Fact]
    public void Booking_dates_are_stored_as_sql_date_columns()
    {
        using var context = CreateContext();

        var bookingEntity = context.Model.FindEntityType(typeof(Booking));

        Assert.Equal(
            "date",
            bookingEntity?.FindProperty(nameof(Booking.CheckInDate))?.GetColumnType());
        Assert.Equal(
            "date",
            bookingEntity?.FindProperty(nameof(Booking.CheckOutDate))?.GetColumnType());
    }

    private static HotelBookingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HotelBookingDbContext>()
            .UseSqlServer("Server=localhost;Database=HotelBookingTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new HotelBookingDbContext(options);
    }
}
