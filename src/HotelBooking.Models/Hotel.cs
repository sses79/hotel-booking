namespace HotelBooking.Models;

public sealed class Hotel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public ICollection<Room> Rooms { get; set; } = [];
}
