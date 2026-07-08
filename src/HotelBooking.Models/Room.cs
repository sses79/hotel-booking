namespace HotelBooking.Models;

public sealed class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HotelId { get; set; }

    public Hotel? Hotel { get; set; }

    public required string RoomNumber { get; set; }

    public RoomType RoomType { get; set; }

    public int Capacity { get; set; }

    public ICollection<Booking> Bookings { get; set; } = [];
}
