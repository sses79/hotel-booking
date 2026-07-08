namespace HotelBooking.Services.Bookings;

public enum BookingCreateStatus
{
    Created,
    InvalidDateRange,
    InvalidGuestCount,
    HotelNotFound,
    NoRoomAvailable
}
