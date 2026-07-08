using HotelBooking.Api.Dtos.Bookings;
using HotelBooking.Services.Bookings;
using Microsoft.AspNetCore.Mvc;

namespace HotelBooking.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(IBookingService bookingService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> Create(
        CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bookingService.CreateAsync(
            new CreateBookingCommand(
                request.HotelId,
                request.GuestName,
                request.GuestCount,
                request.CheckInDate,
                request.CheckOutDate,
                request.RoomType),
            cancellationToken);

        if (result.Booking is not null)
        {
            var response = ToResponse(result.Booking);

            return CreatedAtAction(
                nameof(GetByReference),
                new { reference = response.BookingReference },
                response);
        }

        return result.Status switch
        {
            BookingCreateStatus.InvalidDateRange or BookingCreateStatus.InvalidGuestCount =>
                BadRequestProblem(result.ErrorMessage),
            BookingCreateStatus.HotelNotFound =>
                NotFoundProblem(result.ErrorMessage),
            BookingCreateStatus.NoRoomAvailable =>
                ConflictProblem(result.ErrorMessage),
            _ => Problem(result.ErrorMessage)
        };
    }

    [HttpGet("{reference}")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingResponse>> GetByReference(
        string reference,
        CancellationToken cancellationToken)
    {
        var booking = await bookingService.GetByReferenceAsync(reference, cancellationToken);

        if (booking is null)
        {
            return NotFoundProblem("Booking was not found.");
        }

        return Ok(ToResponse(booking));
    }

    private ActionResult BadRequestProblem(string? message)
    {
        return Problem(message, statusCode: StatusCodes.Status400BadRequest);
    }

    private ActionResult NotFoundProblem(string? message)
    {
        return Problem(message, statusCode: StatusCodes.Status404NotFound);
    }

    private ActionResult ConflictProblem(string? message)
    {
        return Problem(message, statusCode: StatusCodes.Status409Conflict);
    }

    private static BookingResponse ToResponse(BookingDetailsResult booking)
    {
        return new BookingResponse(
            booking.BookingReference,
            booking.HotelId,
            booking.HotelName,
            booking.RoomId,
            booking.RoomNumber,
            booking.RoomType,
            booking.RoomCapacity,
            booking.GuestName,
            booking.GuestCount,
            booking.CheckInDate,
            booking.CheckOutDate,
            booking.CreatedAtUtc);
    }
}
