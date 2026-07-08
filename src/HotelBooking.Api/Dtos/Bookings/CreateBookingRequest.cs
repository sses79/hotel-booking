using System.ComponentModel.DataAnnotations;
using HotelBooking.Models;

namespace HotelBooking.Api.Dtos.Bookings;

public sealed record CreateBookingRequest(
    [Required] Guid HotelId,
    [Required]
    [MinLength(1)]
    [MaxLength(200)]
    string GuestName,
    [Range(1, int.MaxValue)]
    int GuestCount,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    RoomType? RoomType);
