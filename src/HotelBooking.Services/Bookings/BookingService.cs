using HotelBooking.Models;
using HotelBooking.Repository.Data;
using HotelBooking.Services.Rooms;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.Services.Bookings;

public sealed class BookingService(
    HotelBookingDbContext dbContext,
    IRoomAvailabilityService roomAvailabilityService)
    : IBookingService
{
    public async Task<CreateBookingResult> CreateAsync(
        CreateBookingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!BookingRules.HasValidDateRange(command.CheckInDate, command.CheckOutDate))
        {
            return CreateBookingResult.Failed(
                BookingCreateStatus.InvalidDateRange,
                "Check-in date must be before check-out date.");
        }

        if (!BookingRules.HasValidGuestCount(command.GuestCount))
        {
            return CreateBookingResult.Failed(
                BookingCreateStatus.InvalidGuestCount,
                "Guest count must be at least 1.");
        }

        var hotelExists = await dbContext.Hotels
            .AnyAsync(hotel => hotel.Id == command.HotelId, cancellationToken);

        if (!hotelExists)
        {
            return CreateBookingResult.Failed(
                BookingCreateStatus.HotelNotFound,
                "Hotel was not found.");
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var room = await FindFirstAvailableRoomAsync(command, cancellationToken);

            if (room is null)
            {
                return CreateBookingResult.Failed(
                    BookingCreateStatus.NoRoomAvailable,
                    "No room is available for the requested stay and guest count.");
            }

            var booking = new Booking
            {
                BookingReference = await GenerateBookingReferenceAsync(cancellationToken),
                HotelId = command.HotelId,
                RoomId = room.Id,
                GuestName = command.GuestName.Trim(),
                GuestCount = command.GuestCount,
                CheckInDate = command.CheckInDate,
                CheckOutDate = command.CheckOutDate
            };

            dbContext.Bookings.Add(booking);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var details = await GetByReferenceAsync(booking.BookingReference, cancellationToken);

            return CreateBookingResult.Created(details!);
        });
    }

    public async Task<BookingDetailsResult?> GetByReferenceAsync(
        string bookingReference,
        CancellationToken cancellationToken = default)
    {
        var normalizedReference = bookingReference.Trim();

        return await dbContext.Bookings
            .AsNoTracking()
            .Where(booking => booking.BookingReference == normalizedReference)
            .Select(booking => new BookingDetailsResult(
                booking.BookingReference,
                booking.HotelId,
                booking.Room!.Hotel!.Name,
                booking.RoomId,
                booking.Room.RoomNumber,
                booking.Room.RoomType,
                booking.Room.Capacity,
                booking.GuestName,
                booking.GuestCount,
                booking.CheckInDate,
                booking.CheckOutDate,
                booking.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<Room?> FindFirstAvailableRoomAsync(
        CreateBookingCommand command,
        CancellationToken cancellationToken)
    {
        var availableRooms = await roomAvailabilityService.FindAvailableRoomsAsync(
            command.HotelId,
            command.CheckInDate,
            command.CheckOutDate,
            command.GuestCount,
            command.RoomType,
            cancellationToken);

        var roomId = availableRooms.FirstOrDefault()?.Id;

        if (roomId is null)
        {
            return null;
        }

        return await dbContext.Rooms.SingleAsync(room => room.Id == roomId, cancellationToken);
    }

    private async Task<string> GenerateBookingReferenceAsync(CancellationToken cancellationToken)
    {
        string reference;

        do
        {
            reference = $"HB-{Random.Shared.Next(100_000, 1_000_000)}";
        }
        while (await dbContext.Bookings.AnyAsync(
            booking => booking.BookingReference == reference,
            cancellationToken));

        return reference;
    }
}
