namespace HotelBooking.Services.Bookings;

public sealed record CreateBookingResult(
    BookingCreateStatus Status,
    BookingDetailsResult? Booking,
    string? ErrorMessage)
{
    public static CreateBookingResult Created(BookingDetailsResult booking)
    {
        return new CreateBookingResult(BookingCreateStatus.Created, booking, null);
    }

    public static CreateBookingResult Failed(BookingCreateStatus status, string errorMessage)
    {
        return new CreateBookingResult(status, null, errorMessage);
    }
}
