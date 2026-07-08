namespace HotelBooking.Services.Bookings;

public interface IBookingService
{
    Task<CreateBookingResult> CreateAsync(
        CreateBookingCommand command,
        CancellationToken cancellationToken = default);

    Task<BookingDetailsResult?> GetByReferenceAsync(
        string bookingReference,
        CancellationToken cancellationToken = default);
}
