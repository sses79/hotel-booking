using HotelBooking.Api.Dtos.Hotels;
using HotelBooking.Api.Dtos.Rooms;
using HotelBooking.Models;
using HotelBooking.Services;
using HotelBooking.Services.Hotels;
using HotelBooking.Services.Rooms;
using Microsoft.AspNetCore.Mvc;

namespace HotelBooking.Api.Controllers;

[ApiController]
[Route("api/hotels")]
public sealed class HotelsController(
    IHotelSearchService hotelSearchService,
    IRoomAvailabilityService roomAvailabilityService,
    TimeProvider timeProvider)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HotelResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HotelResponse>>> Search(
        [FromQuery] string? name,
        CancellationToken cancellationToken)
    {
        var hotels = await hotelSearchService.SearchAsync(name, cancellationToken);

        return Ok(hotels
            .Select(hotel => new HotelResponse(hotel.Id, hotel.Name))
            .ToList());
    }

    [HttpGet("{hotelId:guid}/rooms/available")]
    [ProducesResponseType<IReadOnlyList<AvailableRoomResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<AvailableRoomResponse>>> FindAvailableRooms(
        Guid hotelId,
        [FromQuery] DateOnly checkIn,
        [FromQuery] DateOnly checkOut,
        [FromQuery] int guests,
        [FromQuery] RoomType? roomType,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (!BookingRules.HasFutureCheckInDate(checkIn, today))
        {
            return Problem("Check-in date must be in the future.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!BookingRules.HasValidDateRange(checkIn, checkOut))
        {
            return Problem("Check-in date must be before check-out date.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!BookingRules.HasValidGuestCount(guests))
        {
            return Problem("Guest count must be at least 1.", statusCode: StatusCodes.Status400BadRequest);
        }

        var rooms = await roomAvailabilityService.FindAvailableRoomsAsync(
            hotelId,
            checkIn,
            checkOut,
            guests,
            roomType,
            cancellationToken);

        return Ok(rooms
            .Select(room => new AvailableRoomResponse(
                room.Id,
                room.HotelId,
                room.RoomNumber,
                room.RoomType,
                room.Capacity))
            .ToList());
    }
}
