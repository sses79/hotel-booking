# AGENTS.md

## Scope

This repository is for solving the backend developer challenge in
`challenge.md`.

Build a practical hotel room booking API using ASP.NET Core, C#, and EF Core.
Do not turn this into a large platform or a fancy architecture exercise. The
goal is to solve the challenge clearly, correctly, and in a way that is easy for
a reviewer to run and discuss.

## Priorities

- Implement the required RESTful API.
- Make booking and availability rules correct.
- Keep the project structure simple.
- Provide Swagger/OpenAPI for manual testing.
- Add seed/reset API functionality for testing.
- Add focused automated tests for risky rules.
- Document how to run, test, seed, reset, and verify the API.

## Preferred Project Shape

Use one solution with a few focused projects. This shows how the solution is
broken down without becoming a large clean-architecture exercise:

```text
HotelBooking.sln
src/
  HotelBooking.Api/
  HotelBooking.Models/
  HotelBooking.Services/
tests/
  HotelBooking.UnitTests/
  HotelBooking.IntegrationTests/
docs/
```

Project responsibilities:

- `HotelBooking.Api`: REST controllers, Swagger/OpenAPI, health endpoint,
  request/response DTOs, configuration, dependency registration.
- `HotelBooking.Models`: simple domain/data models such as `Hotel`, `Room`,
  `Booking`, `RoomType`, and shared request/result types where useful.
- `HotelBooking.Services`: booking/search use cases, EF Core `DbContext`,
  database setup, seed/reset logic, and persistence queries.
- `HotelBooking.UnitTests`: fast tests for date overlap, capacity, room
  selection, and booking-rule behavior.
- `HotelBooking.IntegrationTests`: API and SQLite-backed EF Core tests for
  seed/reset, availability, booking creation, and lookup.

Avoid adding separate `Application`, `Domain`, and `Infrastructure` projects.
For this challenge, `Models` and `Services` are enough separation.

## Business Rules

The implementation must support:

- Hotels have 3 room types: single, double, deluxe.
- Hotels have 6 rooms.
- A room cannot be double booked for any given night.
- A booking must not require guests to change rooms during their stay.
- Booking references must be unique.
- A room cannot be occupied by more people than its capacity.

Use half-open date ranges for bookings:

```text
[checkInDate, checkOutDate)
```

Overlap rule:

```csharp
existing.CheckInDate < requested.CheckOutDate
    && requested.CheckInDate < existing.CheckOutDate
```

## API Requirements

Implement endpoints for:

- Find a hotel by name.
- Find available rooms between two dates for a given number of guests.
- Book a room.
- Find booking details by booking reference number.
- Seed test data.
- Reset all data.
- Health check.

Suggested endpoints:

```text
GET  /api/hotels?name=...
GET  /api/hotels/{hotelId}/rooms/available?checkIn=...&checkOut=...&guests=...
POST /api/bookings
GET  /api/bookings/{reference}
POST /api/admin/seed
POST /api/admin/reset
GET  /health
```

## Seed And Reset

Use Option A:

```text
POST /api/admin/seed = reset first, then seed predictable data
```

Default seed data should be just enough for testing:

```text
Hotel:
  Grand Plaza Hotel

Rooms:
  101 single capacity 1
  102 single capacity 1
  201 double capacity 2
  202 double capacity 2
  301 deluxe capacity 4
  302 deluxe capacity 4
```

Do not seed bookings by default. Let reviewers create bookings through the real
booking endpoint.

## Data And Testing

- Prefer SQLite for local development and integration tests.
- Do not rely only on EF Core InMemory for relational behavior.
- Add tests for availability, overlap, back-to-back bookings, capacity,
  booking reference lookup, seed, and reset.
- Keep controllers thin by moving booking and search behavior into
  `HotelBooking.Services`.

## Azure

Azure hosting is optional because the challenge says it is not critical.

If Azure deployment is added, prefer the low-idle-cost path:

```text
ghcr.io/sses79/hotel-booking-api:<commit-sha>
  -> Azure Container Apps Consumption
  -> Azure SQL Database serverless
```

Use GitHub Container Registry like `tfl-analytics`. Keep the package public
while the repo is public so Azure Container Apps can pull anonymously. Deploy
commit-SHA image tags, not `latest`.

App Service Free F1 may be used only as a quick demo if the subscription,
region, quota, and runtime support it. Do not make the solution depend on Free
F1 being available.

## Avoid

- A frontend.
- Authentication.
- Overly broad clean architecture.
- Event-driven infrastructure.
- Message queues.
- Cosmos DB.
- SignalR.
- Complex Azure infrastructure beyond what is useful for this challenge.
- Large abstractions that do not directly help the booking API.
