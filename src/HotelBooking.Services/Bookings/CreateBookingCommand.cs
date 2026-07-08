using HotelBooking.Models;

namespace HotelBooking.Services.Bookings;

public sealed record CreateBookingCommand(
    Guid HotelId,
    string GuestName,
    int GuestCount,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    RoomType? RoomType);
