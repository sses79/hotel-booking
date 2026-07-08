using HotelBooking.Models;

namespace HotelBooking.Services.Bookings;

public sealed record BookingDetailsResult(
    string BookingReference,
    Guid HotelId,
    string HotelName,
    Guid RoomId,
    string RoomNumber,
    RoomType RoomType,
    int RoomCapacity,
    string GuestName,
    int GuestCount,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    DateTimeOffset CreatedAtUtc);
