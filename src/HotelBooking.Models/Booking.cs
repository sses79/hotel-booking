namespace HotelBooking.Models;

public sealed class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string BookingReference { get; set; }

    public Guid HotelId { get; set; }

    public Guid RoomId { get; set; }

    public Room? Room { get; set; }

    public required string GuestName { get; set; }

    public int GuestCount { get; set; }

    public DateOnly CheckInDate { get; set; }

    public DateOnly CheckOutDate { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
