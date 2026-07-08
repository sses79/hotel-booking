namespace HotelBooking.Api.Dtos.TestData;

public sealed record SeedResponse(
    Guid HotelId,
    string HotelName,
    int RoomsCreated);
