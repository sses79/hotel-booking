using HotelBooking.Models;

namespace HotelBooking.Api.Dtos.Bookings;

public sealed record BookingResponse(
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
