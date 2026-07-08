# Hotel Booking API Implementation Plan

## Goal

Build a RESTful hotel room booking API using ASP.NET Core and Entity Framework
Core. The API should support hotel search, room availability search, room
booking, booking lookup, Swagger/OpenAPI testing, seed/reset endpoints, focused
automated tests, and an optional Azure deployment path.

The main delivery principle is the 80/20 rule: make the booking rules correct,
easy to review, and easy to test before adding extra platform detail.

## Architecture

Use one solution with a few focused projects. The challenge asks reviewers to
understand how the solution is broken down, so this structure gives clear
separation without turning the work into a large architecture exercise:

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

Responsibilities:

- `HotelBooking.Api`: controllers, request/response DTOs, Swagger, health
  endpoint, API validation responses, and dependency registration.
- `HotelBooking.Models`: `Hotel`, `Room`, `Booking`, `RoomType`, shared enums,
  and simple result/request types where useful.
- `HotelBooking.Services`: hotel search, room availability, booking creation,
  booking lookup, seed/reset orchestration, EF Core `DbContext`, migrations,
  and persistence queries.
- `HotelBooking.UnitTests`: fast tests for date overlap, capacity, deterministic
  room selection, and booking-rule behavior.
- `HotelBooking.IntegrationTests`: API and SQLite-backed EF Core tests for
  seed/reset, availability, booking creation, and booking lookup.

Avoid separate `Application`, `Domain`, and `Infrastructure` projects. For this
challenge, `Models` and `Services` are enough decomposition to show design
thinking while keeping the implementation easy to review.

## Domain Model

Core entities:

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

RoomType
  Single
  Double
  Deluxe
```

Use `DateOnly` for booking dates. A booking occupies nights from
`CheckInDate` up to, but not including, `CheckOutDate`:

```text
[checkInDate, checkOutDate)
```

This allows back-to-back bookings. For example, a booking ending on August 3
does not overlap another booking starting on August 3.

EF Core configuration should include:

- Required hotel, room, and booking fields.
- Room type stored as a readable string.
- A unique index on `BookingReference`.
- Relationships from hotel to rooms and room to bookings.
- SQLite for local development by default.

## API Surface

Expose the required functionality through predictable REST endpoints:

```text
GET  /api/hotels?name=...
GET  /api/hotels/{hotelId}/rooms/available?checkIn=...&checkOut=...&guests=...
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

`roomType` may be optional. If supplied, booking should only consider that room
type. If omitted, choose the smallest suitable room by capacity and a stable room
type order.

## Seed And Reset Plan

Expose seed/reset through API endpoints because the challenge explicitly asks
for testing functionality.

```text
POST /api/admin/reset
POST /api/admin/seed
```

`POST /api/admin/reset` should remove all data in dependency order:

```text
Bookings -> Rooms -> Hotels
```

It should return `204 No Content` or a small `200 OK` result after the database
is empty.

`POST /api/admin/seed` should use Option A: reset first, then seed predictable
test data. This makes repeated reviewer testing deterministic.

Seed data:

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

Do not seed bookings by default. Reviewers should create bookings through
`POST /api/bookings` so the real booking workflow is exercised.

The seed endpoint should return useful identifiers:

```json
{
  "hotelId": "00000000-0000-0000-0000-000000000001",
  "hotelName": "Grand Plaza Hotel",
  "roomsCreated": 6
}
```

## Booking Algorithm

Booking creation is the most important workflow.

1. Validate that `checkInDate < checkOutDate`.
2. Validate that `guestCount >= 1`.
3. Load the requested hotel.
4. Find candidate rooms by hotel, optional room type, and capacity.
5. Exclude rooms with overlapping bookings.
6. Pick one deterministic room.
7. Generate a unique booking reference.
8. Start a database transaction.
9. Recheck availability inside the transaction.
10. Save the booking.
11. Return `201 Created` with the booking details.

Use this overlap rule:

```csharp
existing.CheckInDate < requested.CheckOutDate
    && requested.CheckInDate < existing.CheckOutDate
```

If that expression is true, the two date ranges overlap and the room cannot be
used for the requested stay.

For production-level double-booking prevention, document that stricter database
locking, serializable isolation, or provider-specific constraints may be needed.
For the challenge, a transaction plus an internal availability recheck is a
clear and practical approach.

## Implementation Phases

1. Create `HotelBooking.sln`, the `Api`, `Models`, `Services`, `UnitTests`, and
   `IntegrationTests` projects, Swagger, and `/health`.
2. Add models and EF Core configuration.
3. Add seed/reset support in `HotelBooking.Services` with one hotel and six
   rooms across single, double, and deluxe room types.
4. Add hotel search by name.
5. Add available-room search by date range and guest count.
6. Add booking creation with overlap, capacity, and one-room-per-stay rules.
7. Add booking lookup by booking reference.
8. Add unit and integration tests for the risky rules.
9. Add README instructions and example API requests.
10. Optionally add Azure/Bicep deployment support.

## Azure Deployment Plan

Azure hosting is optional because the challenge says it is not a critical
requirement. If implemented, keep it small and repeatable.

Recommended low-idle-cost Azure shape:

```text
Swagger / API consumer
  -> ghcr.io/sses79/hotel-booking-api:<commit-sha>
  -> Azure Container Apps Consumption
  -> Azure SQL Database serverless
  -> optional Application Insights
```

Recommended infrastructure:

- Azure Container Apps Consumption for the API, configured with `minReplicas: 0`
  and a small `maxReplicas` value.
- A `Dockerfile` for `HotelBooking.Api`.
- GitHub Actions builds and pushes the API image to GitHub Container Registry:
  `ghcr.io/sses79/hotel-booking-api:<commit-sha>`.
- Keep the GHCR package public while this repository is public so Azure
  Container Apps can pull the image anonymously, avoiding ACR cost and registry
  credentials.
- Deploy immutable commit-SHA tags. Do not rely on `latest` for Azure
  deployments.
- Azure SQL serverless for the hosted EF Core database.
- Optional Key Vault for secrets.
- Optional Application Insights for diagnostics.
- Bicep parameters for environment-specific values.

App Service Free F1 can still be used as a quick demo path if the subscription,
region, quota, and required runtime support it. Do not make the deployment plan
depend on Free F1 being available. The Container Apps path mirrors the
`tfl-analytics` approach and avoids always-on App Service cost for a low-traffic
challenge API.

Deployment workflow:

```bash
az bicep build --file infra/bicep/main.bicep

az deployment group what-if \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam

az deployment group validate \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam

az deployment group create \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam \
  --output table
```

After deployment, verify:

- `/health` returns healthy.
- Swagger opens.
- Seed and reset endpoints work.
- Hotel search works.
- Availability search works.
- Booking creation works.
- Booking lookup returns the created booking.

## Test Plan

Automated coverage should focus on the rules most likely to break:

- Hotel search by name returns the expected hotel.
- Availability excludes overlapping bookings.
- Back-to-back bookings are allowed.
- Guest count cannot exceed room capacity.
- A booking uses one room for the whole stay.
- Booking reference is unique.
- Booking lookup returns the created booking.
- Seed creates the expected hotel and six rooms.
- Reset clears all data.
- Swagger is available for manual testing.

Use `HotelBooking.UnitTests` for pure rules such as date overlap. Use
`HotelBooking.IntegrationTests` for EF Core, seed/reset, and API behavior.
Prefer SQLite-backed integration tests over EF Core InMemory when testing
relational behavior.

## Reviewer Notes

The implementation should be intentionally small. The goal is not to build a
complete hotel-management system; it is to show clear reasoning, correct booking
rules, testability, and a practical path to deployment.

Key trade-offs to document in the README:

- SQLite is used locally for speed and realistic relational behavior.
- Azure SQL is the natural hosted database if Azure deployment is added.
- Authentication is intentionally omitted because the challenge requires none.
- Seed/reset endpoints are included because the challenge requests test data
  setup.
- Stronger race-condition handling is a production concern and can be improved
  with database-specific locking or constraints.
