# Solve Challenge Guide

This guide is for solving the hotel booking API challenge in `challenge.md`.
Start with the business rules, protect behavior with tests, keep the API thin,
and put important decisions in named domain code.

The 80/20 rule here: most of the value comes from a small number of choices.
Model the domain clearly, make availability correct, keep booking creation
transactional, expose simple REST endpoints, and document how to run and test
the API.

## What The Reviewer Cares About

The brief says this is not only about a fully working app. It is also about how
you think. A good submission should make these things obvious:

- You understood the booking rules.
- You chose a simple, defensible design.
- You can explain trade-offs.
- The API is easy to run and test.
- The risky rules have automated tests.
- Supporting docs help the reviewer move quickly.

Do not try to build a hotel platform. Build the smallest hotel room booking API
that proves the rules.

## Core Workflow Lessons

A clean challenge workflow should:

- Capture baseline behavior before changing code.
- Split calculation, formatting, and rules into separate classes.
- Give business rules explicit names.
- Keep the interface layer thin.
- Add tests for edge cases, not only happy paths.
- Write short docs that explain decisions.

For this challenge, the equivalent is:

- Keep controllers thin.
- Put booking rules in services or domain methods.
- Keep EF Core persistence separate from rule decisions.
- Test availability and booking overlap rules directly.
- Include a README and these guide notes.

## Recommended Project Shape

Use one solution with a few focused projects:

```text
HotelBooking.sln
src/
  HotelBooking.Api/
  HotelBooking.Models/
  HotelBooking.Repository/
  HotelBooking.Services/
tests/
  HotelBooking.UnitTests/
  HotelBooking.IntegrationTests/
docs/
```

This gives reviewers a clear view of how the solution is broken down without
adding a full clean-architecture stack.

Suggested responsibilities:

- `HotelBooking.Api`: controllers, request/response DTOs, Swagger, health
  endpoint, configuration, and dependency registration.
- `HotelBooking.Models`: hotel, room, booking, room type, capacity, and shared
  model/result types where useful.
- `HotelBooking.Repository`: EF Core `DbContext`, SQL Server migrations,
  persistence queries, and data access helpers.
- `HotelBooking.Services`: booking/search use cases, seeding/reset, and
  booking-rule behavior.
- `HotelBooking.UnitTests`: fast tests for date overlap, capacity, room
  selection, and booking-rule behavior.
- `HotelBooking.IntegrationTests`: SQL Server-backed EF Core and API tests.

## Domain Model

Keep the model small and explicit:

```text
Hotel
  Id
  Name
  Rooms

Room
  Id
  HotelId
  RoomNumber
  RoomType
  Capacity

Booking
  Id
  BookingReference
  HotelId
  RoomId
  GuestName
  GuestCount
  CheckInDate
  CheckOutDate
  CreatedAtUtc
```

Use `DateOnly` for stay dates if the project targets a modern .NET version.
The booking occupies nights from `CheckInDate` up to, but not including,
`CheckOutDate`. This half-open range is the simplest way to avoid ambiguity:

```text
occupied nights: [check-in, check-out)
```

Example: July 1 to July 3 occupies July 1 and July 2. A new booking starting
July 3 does not overlap.

## Core Rules

Model these rules as named checks:

- A hotel has six rooms.
- Room types are `single`, `double`, and `deluxe`.
- A room has a maximum guest capacity.
- A room cannot be double booked for an overlapping night.
- A booking must stay in one room for the full date range.
- Booking references must be unique.
- Guest count must be less than or equal to room capacity.

The most important rule is overlap. Use this condition:

```csharp
existing.CheckInDate < requested.CheckOutDate
    && requested.CheckInDate < existing.CheckOutDate
```

That means the date ranges overlap. To find available rooms, exclude rooms with
any booking matching that condition.

## API Endpoints

Use boring, predictable REST endpoints:

```text
GET  /api/hotels?name=Grand
GET  /api/hotels/{hotelId}/rooms/available?checkIn=2026-08-01&checkOut=2026-08-03&guests=2
POST /api/bookings
GET  /api/bookings/{reference}
POST /api/admin/seed
POST /api/admin/reset
GET  /health
```

Example booking request:

```json
{
  "hotelId": "00000000-0000-0000-0000-000000000001",
  "guestName": "Ada Lovelace",
  "guestCount": 2,
  "checkInDate": "2026-08-01",
  "checkOutDate": "2026-08-03",
  "roomType": "double"
}
```

The `roomType` can be optional. If supplied, search only that room type. If not
supplied, choose the cheapest/smallest suitable room by capacity and type order.
Document whichever choice is made.

## Booking Flow

Booking is the one operation that must be careful:

1. Validate dates: check-in must be today or later and before check-out.
2. Validate guest count: at least 1 guest.
3. Find the hotel.
4. Find rooms with enough capacity.
5. Remove rooms that overlap existing bookings.
6. Pick one room deterministically.
7. Generate a unique booking reference.
8. Save the booking in a serializable transaction with bounded retries.
9. Return `201 Created` with the booking details.

Concurrent requests can target the same final room. Enforce the rule in the
database transaction:

- Add an index on `BookingReference`.
- Use serializable isolation for booking creation.
- Run the complete transaction inside SQL Server's retry strategy.
- Test two requests competing for the final room against real SQL Server.

## EF Core Choices

For the challenge, SQL Server is the best local default because Azure hosting
will use Azure SQL Database:

- It keeps local SQL behavior close to Azure.
- It supports real relational constraints and transactions.
- It avoids SQLite-specific differences in date handling, locking, and SQL
  dialect.
- It can run locally through Docker Compose.

Recommended EF Core setup:

- Use migrations.
- Configure required fields and max lengths.
- Configure relationships explicitly.
- Add a unique index for hotel name if the seed data assumes unique names.
- Add a unique index for booking reference.
- Store `RoomType` as a string for readability.

Do not rely only on EF Core InMemory for rule tests. It does not behave like a
relational database in enough places to hide real bugs.

## Test Strategy

The 80/20 test suite:

- Finding a hotel by name returns the right hotel.
- Available rooms excludes overlapping bookings.
- Back-to-back bookings are allowed.
- Booking fails when guest count exceeds room capacity.
- Booking fails when no single room is available for the whole stay.
- Booking returns a unique reference.
- Lookup by booking reference returns the saved booking.
- Seed creates the expected hotel/rooms.
- Reset removes data.

Use unit tests for pure date/rule logic. Use integration tests for EF Core and
API behavior.

## Implementation Order

Build in this order:

1. Create solution, API project, test project, and Swagger.
2. Add domain entities and `DbContext`.
3. Add seed/reset endpoints.
4. Add hotel search.
5. Add availability search.
6. Add booking creation with overlap checks.
7. Add booking lookup.
8. Add focused tests.
9. Add README with run commands and API examples.
10. Optionally add Azure deployment notes.

This order gives visible progress early and leaves the riskiest logic enough
space for tests.

## Submission README Checklist

Include:

- What the API does.
- Tech stack and database choice.
- How to run locally.
- How to run tests.
- How to open Swagger.
- Seed and reset instructions.
- Example requests.
- Known trade-offs or future improvements.

Good trade-offs to mention:

- SQL Server chosen locally through Docker Compose to stay close to Azure SQL.
- Serializable isolation and SQL Server retries prevent concurrent
  double-booking; idempotency remains a separate production improvement.
- Authentication is intentionally omitted because the brief says it is not
  required.

## Future Codex Notes

When using this guide to implement the challenge later:

- Read `challenge.md` first.
- Keep changes small and testable.
- Prefer correctness of booking rules over extra features.
- Do not add a frontend unless asked.
- Do not hide business rules inside controllers.
- Use Swagger and seed/reset to make reviewer testing easy.
