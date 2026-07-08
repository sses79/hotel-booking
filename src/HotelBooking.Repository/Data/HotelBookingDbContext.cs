using HotelBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.Repository.Data;

public sealed class HotelBookingDbContext(DbContextOptions<HotelBookingDbContext> options)
    : DbContext(options)
{
    public DbSet<Hotel> Hotels => Set<Hotel>();

    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureHotels(modelBuilder);
        ConfigureRooms(modelBuilder);
        ConfigureBookings(modelBuilder);
    }

    private static void ConfigureHotels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hotel>(entity =>
        {
            entity.ToTable("Hotels");

            entity.HasKey(hotel => hotel.Id);

            entity.Property(hotel => hotel.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.HasIndex(hotel => hotel.Name);

            entity.HasMany(hotel => hotel.Rooms)
                .WithOne(room => room.Hotel)
                .HasForeignKey(room => room.HotelId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRooms(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(entity =>
        {
            entity.ToTable("Rooms", table =>
            {
                table.HasCheckConstraint("CK_Rooms_Capacity", "[Capacity] >= 1");
            });

            entity.HasKey(room => room.Id);

            entity.Property(room => room.RoomNumber)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(room => room.RoomType)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(room => room.Capacity)
                .IsRequired();

            entity.HasIndex(room => new { room.HotelId, room.RoomNumber })
                .IsUnique();

            entity.HasIndex(room => new { room.HotelId, room.RoomType, room.Capacity });
        });
    }

    private static void ConfigureBookings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings", table =>
            {
                table.HasCheckConstraint("CK_Bookings_DateRange", "[CheckInDate] < [CheckOutDate]");
                table.HasCheckConstraint("CK_Bookings_GuestCount", "[GuestCount] >= 1");
            });

            entity.HasKey(booking => booking.Id);

            entity.Property(booking => booking.BookingReference)
                .HasMaxLength(30)
                .IsRequired();

            entity.HasIndex(booking => booking.BookingReference)
                .IsUnique();

            entity.Property(booking => booking.GuestName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(booking => booking.GuestCount)
                .IsRequired();

            entity.Property(booking => booking.CheckInDate)
                .HasColumnType("date")
                .IsRequired();

            entity.Property(booking => booking.CheckOutDate)
                .HasColumnType("date")
                .IsRequired();

            entity.Property(booking => booking.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(booking => new
            {
                booking.HotelId,
                booking.RoomId,
                booking.CheckInDate,
                booking.CheckOutDate
            });

            entity.HasOne(booking => booking.Room)
                .WithMany(room => room.Bookings)
                .HasForeignKey(booking => booking.RoomId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Hotel>()
                .WithMany()
                .HasForeignKey(booking => booking.HotelId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
