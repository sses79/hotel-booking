using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HotelBooking.IntegrationTests.Infrastructure;
using HotelBooking.Models;
using HotelBooking.Repository.Data;
using HotelBooking.Services.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelBooking.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class ApiEndpointTests(SqlServerFixture sqlServer)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task OpenApi_endpoint_is_available()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_is_available()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Seed_then_search_hotels_returns_seeded_hotel()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();

        using var seedResponse = await client.PostAsync("/api/admin/seed", null);
        var hotels = await GetFromJsonAsync<List<HotelDto>>(
            client,
            "/api/hotels?name=Grand",
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);
        var hotel = Assert.Single(hotels!);
        Assert.Equal(SeedData.GrandPlazaHotelId, hotel.Id);
        Assert.Equal(SeedData.GrandPlazaHotelName, hotel.Name);
    }

    [Fact]
    public async Task Availability_returns_rooms_matching_guest_count()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var checkOut = checkIn.AddDays(2);

        await client.PostAsync("/api/admin/seed", null);

        var rooms = await GetFromJsonAsync<List<AvailableRoomDto>>(
            client,
            $"/api/hotels/{SeedData.GrandPlazaHotelId}/rooms/available?checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}&guests=2",
            JsonOptions);

        Assert.Equal(["201", "202", "301", "302"], rooms!.Select(room => room.RoomNumber));
    }

    [Fact]
    public async Task Availability_rejects_check_in_date_that_is_in_the_past()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var response = await client.GetAsync(
            $"/api/hotels/{SeedData.GrandPlazaHotelId}/rooms/available?checkIn={today.AddDays(-1):yyyy-MM-dd}&checkOut={today.AddDays(1):yyyy-MM-dd}&guests=2");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Availability_allows_check_in_today()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await client.PostAsync("/api/admin/seed", null);

        using var response = await client.GetAsync(
            $"/api/hotels/{SeedData.GrandPlazaHotelId}/rooms/available?checkIn={today:yyyy-MM-dd}&checkOut={today.AddDays(1):yyyy-MM-dd}&guests=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Booking_workflow_creates_and_returns_booking()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var checkOut = checkIn.AddDays(2);

        await client.PostAsync("/api/admin/seed", null);

        var createResponse = await client.PostAsJsonAsync(
            "/api/bookings",
            new
            {
                HotelId = SeedData.GrandPlazaHotelId,
                GuestName = "Ada Lovelace",
                GuestCount = 2,
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                RoomType = RoomType.Double
            },
            JsonOptions);

        var createdBooking = await ReadFromJsonAsync<BookingDto>(createResponse, JsonOptions);
        var lookupBooking = await GetFromJsonAsync<BookingDto>(
            client,
            $"/api/bookings/{createdBooking!.BookingReference}",
            JsonOptions);
        var remainingRooms = await GetFromJsonAsync<List<AvailableRoomDto>>(
            client,
            $"/api/hotels/{SeedData.GrandPlazaHotelId}/rooms/available?checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}&guests=2",
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.StartsWith("HB-", createdBooking.BookingReference, StringComparison.Ordinal);
        Assert.Equal("Ada Lovelace", lookupBooking!.GuestName);
        Assert.Equal(createdBooking.BookingReference, lookupBooking.BookingReference);
        Assert.Equal("201", lookupBooking.RoomNumber);
        Assert.DoesNotContain(remainingRooms!, room => room.RoomNumber == "201");
    }

    [Fact]
    public async Task Booking_rejects_check_in_date_that_is_in_the_past()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new
            {
                HotelId = SeedData.GrandPlazaHotelId,
                GuestName = "Ada Lovelace",
                GuestCount = 2,
                CheckInDate = today.AddDays(-1),
                CheckOutDate = today.AddDays(1),
                RoomType = RoomType.Double
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Booking_allows_check_in_today()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await client.PostAsync("/api/admin/seed", null);

        using var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new
            {
                HotelId = SeedData.GrandPlazaHotelId,
                GuestName = "Same-day guest",
                GuestCount = 2,
                CheckInDate = today,
                CheckOutDate = today.AddDays(1),
                RoomType = RoomType.Double
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_requests_cannot_double_book_the_last_available_room()
    {
        var barrier = new AvailabilityBarrierInterceptor();
        await using var factory = new HotelBookingApiFactory(sqlServer, barrier);
        using var client = factory.CreateClient();
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);
        var checkOut = checkIn.AddDays(2);

        await client.PostAsync("/api/admin/seed", null);

        using var firstBooking = await CreateBookingAsync(
            client,
            "First guest",
            checkIn,
            checkOut);
        Assert.Equal(HttpStatusCode.Created, firstBooking.StatusCode);

        barrier.Arm();

        var concurrentResponses = await Task.WhenAll(
            CreateBookingAsync(client, "Second guest", checkIn, checkOut),
            CreateBookingAsync(client, "Third guest", checkIn, checkOut));

        try
        {
            Assert.Equal(
                1,
                concurrentResponses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(
                1,
                concurrentResponses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HotelBookingDbContext>();
            var bookings = await dbContext.Bookings
                .Where(booking =>
                    booking.CheckInDate < checkOut
                    && checkIn < booking.CheckOutDate)
                .ToListAsync();

            Assert.Equal(2, bookings.Count);
            Assert.Equal(2, bookings.Select(booking => booking.RoomId).Distinct().Count());
        }
        finally
        {
            foreach (var response in concurrentResponses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Reset_clears_seeded_data()
    {
        await using var factory = new HotelBookingApiFactory(sqlServer);
        using var client = factory.CreateClient();

        await client.PostAsync("/api/admin/seed", null);
        using var resetResponse = await client.PostAsync("/api/admin/reset", null);
        var hotels = await GetFromJsonAsync<List<HotelDto>>(client, "/api/hotels", JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);
        Assert.Empty(hotels!);
    }

    private sealed record HotelDto(Guid Id, string Name);

    private sealed record AvailableRoomDto(
        Guid Id,
        Guid HotelId,
        string RoomNumber,
        RoomType RoomType,
        int Capacity);

    private sealed record BookingDto(
        string BookingReference,
        Guid HotelId,
        string HotelName,
        Guid RoomId,
        string RoomNumber,
        RoomType RoomType,
        int RoomCapacity,
        string GuestName,
        int GuestCount,
        DateOnly CheckInDate,
        DateOnly CheckOutDate,
        DateTimeOffset CreatedAtUtc);

    private static async Task<T?> GetFromJsonAsync<T>(
        HttpClient client,
        string requestUri,
        JsonSerializerOptions jsonOptions)
    {
        using var response = await client.GetAsync(requestUri);

        return await ReadFromJsonAsync<T>(response, jsonOptions);
    }

    private static async Task<T?> ReadFromJsonAsync<T>(
        HttpResponseMessage response,
        JsonSerializerOptions jsonOptions)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected success but got {(int)response.StatusCode} {response.StatusCode}: {body}");

        return JsonSerializer.Deserialize<T>(body, jsonOptions);
    }

    private static Task<HttpResponseMessage> CreateBookingAsync(
        HttpClient client,
        string guestName,
        DateOnly checkIn,
        DateOnly checkOut)
    {
        return client.PostAsJsonAsync(
            "/api/bookings",
            new
            {
                HotelId = SeedData.GrandPlazaHotelId,
                GuestName = guestName,
                GuestCount = 2,
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                RoomType = RoomType.Double
            },
            JsonOptions);
    }
}
