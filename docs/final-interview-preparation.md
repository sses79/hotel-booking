# Final Interview Preparation

This guide is for the technical discussion about the hotel-booking challenge. It
is based on the current implementation, not only the original design notes.

## What The Interviewers Are Likely Assessing

The brief explicitly says that the discussion is about how the problem was
broken down, what was considered, and why decisions were made. The most useful
conversation is therefore not a file-by-file recital. Show that you can:

- turn ambiguous business wording into precise rules;
- protect correctness under concurrent requests, not only in a single test;
- choose enough architecture for maintainability without over-engineering;
- distinguish challenge scope from production requirements;
- test the risky behavior at the right boundaries; and
- identify limitations in your own solution without undermining it.

## 90-Second Opening

> I built a small ASP.NET Core and EF Core API around the four required use
> cases: hotel search, room availability, room booking, and booking lookup. I
> used SQL Server locally and in Azure because the most important rule—never
> double-booking a room—is a relational concurrency problem, so I wanted the
> development and test database to behave like production.
>
> I split the solution into API, Services, Repository, and Models projects.
> Controllers translate HTTP requests and responses, services own the use cases
> and booking rules, and the repository project owns EF Core configuration and
> migrations. This provides useful separation without introducing a large
> clean-architecture framework for a small challenge.
>
> Bookings use half-open date ranges, `[check-in, check-out)`, which makes
> back-to-back stays valid. Availability finds one room that can hold the party
> and is free for the whole interval, so guests never move rooms. Booking is
> performed inside a short serializable transaction with SQL Server retry
> handling, preventing concurrent requests from taking the same final room.
> That behavior is verified through a deterministic API integration test using
> a real disposable SQL Server. The solution also provides Swagger, predictable
> seed/reset operations, health checking, Docker Compose, and an optional Azure
> deployment.

Do not memorize this word-for-word. Use it as a route through the discussion:
scope, architecture, business rules, concurrency, tests, and deployment.

## Requirement-To-Code Map

| Requirement | Endpoint | Main implementation |
| --- | --- | --- |
| Find hotel by name | `GET /api/hotels?name=...` | `HotelSearchService` |
| Find available rooms | `GET /api/hotels/{id}/rooms/available` | `RoomAvailabilityService` |
| Book a room | `POST /api/bookings` | `BookingService.CreateAsync` |
| Find booking by reference | `GET /api/bookings/{reference}` | `BookingService.GetByReferenceAsync` |
| Seed predictable data | `POST /api/admin/seed` | `TestDataService.SeedAsync` |
| Reset all data | `POST /api/admin/reset` | `TestDataService.ResetAsync` |
| Manual API testing | `/swagger`, `/openapi/v1.json` | OpenAPI and Swagger registration |
| Health check | `GET /health` | ASP.NET Core health checks |

The seed creates `Grand Plaza Hotel` with stable IDs and six rooms:

| Rooms | Type | Capacity |
| --- | --- | ---: |
| 101, 102 | Single | 1 |
| 201, 202 | Double | 2 |
| 301, 302 | Deluxe | 4 |

## Architecture Walkthrough

```text
HTTP client / Swagger
        |
        v
HotelBooking.Api       controllers, DTOs, HTTP status mapping
        |
        v
HotelBooking.Services  use cases, validation, selection, booking rules
        |
        v
HotelBooking.Repository EF Core DbContext, mappings, indexes, migrations
        |
        v
SQL Server / Azure SQL

HotelBooking.Models is shared by Repository and Services for the small entity model.
```

### Why this structure?

It keeps controllers thin and makes business behavior independently testable,
while avoiding extra `Domain`, `Application`, and `Infrastructure` projects that
would add ceremony to four use cases. The boundaries are practical rather than
dogmatic.

The `Repository` project is a persistence boundary, not an implementation of
the repository pattern. Services use `HotelBookingDbContext` directly. EF Core
already supplies query composition, change tracking, and unit-of-work behavior,
so generic repositories would mostly wrap EF without adding value here.

### Why separate API DTOs from entities?

The HTTP contract should not accidentally change when the persistence model
changes. DTOs also keep navigation properties and EF tracking concerns out of
serialization. Services return focused result records rather than exposing
tracked entities to controllers.

### Why `AsNoTracking`?

Hotel search, availability, and booking lookup are read-only. Disabling change
tracking reduces work and makes that intent explicit.

## Explain The Core Booking Logic

### Date semantics

A stay is represented as a half-open interval:

```text
[checkInDate, checkOutDate)
```

The overlap predicate is:

```csharp
existing.CheckInDate < requested.CheckOutDate
    && requested.CheckInDate < existing.CheckOutDate
```

For an existing booking from 1 August to 3 August:

- 2 August to 4 August overlaps;
- 1 August to 3 August overlaps;
- 3 August to 5 August does not overlap; and
- checkout must always be after check-in.

This model avoids special-case checkout logic and reflects occupied nights.
`DateOnly` is used because a booking is day-based; a time and timezone would add
meaning the domain does not currently need.

The implementation allows check-in today based on the current UTC date. Be
precise about this: `HasNonPastCheckInDate` uses `>= today`. The sentence in
`hotel-booking-challenge-solution.md` saying check-in must be *later than* today
is stale wording; the code and tests permit same-day check-in.

### Capacity and room continuity

Availability starts from rooms in the requested hotel with
`Capacity >= guestCount`, optionally filters to the requested room type, then
excludes any room with an overlapping booking.

The query chooses a single room that is free for the complete requested range.
It does not allocate individual nights, so guests cannot be asked to move rooms
during a stay.

Suitable rooms are ordered by capacity, room type, and room number. This avoids
wasting a larger room when a smaller one is suitable and produces predictable
results and lock acquisition behavior.

### Booking request flow

```text
validate dates and guest count
        |
check that the hotel exists
        |
enter EF execution strategy
        |
begin serializable transaction
        |
query a suitable room free for the whole stay
        |
generate reference and insert booking
        |
commit
        |
read details and return 201 Created + Location
```

If no suitable room remains, the API returns `409 Conflict`. Invalid input is
`400`, a missing hotel or booking is `404`, and successful creation is `201`.
Errors intentionally use the standard `ProblemDetails` shape.

## The Most Important Discussion: Concurrency

### The race condition

An availability check followed by an insert is unsafe under normal
`ReadCommitted` behavior:

```text
Request A                         Request B
---------                         ---------
sees room 202 as free             sees room 202 as free
selects room 202                  selects room 202
inserts booking A                 inserts booking B
commits                           commits
```

A unique booking-reference index cannot prevent this because the conflicting
rows have different references. A conventional unique index also cannot express
"date ranges for this room must never overlap."

Checking availability twice only narrows the timing window. It does not remove
the race if both requests perform their final check before either writes.

### The implemented protection

`BookingService` performs selection and insertion in a short
`IsolationLevel.Serializable` transaction. SQL Server can retain key-range
locks for the availability query, preventing a phantom conflicting booking from
being inserted as though the checked range were still empty.

Serializable transactions can deadlock when concurrent requests both read and
then try to write. The complete transaction is therefore run inside EF Core's
SQL Server execution strategy with at most five retries and a two-second maximum
delay. The change tracker is cleared at the start of each attempt so state from
a failed attempt cannot leak into the replay.

The transaction contains only database work. Keeping it short limits blocking.

### How the concurrency test is stronger than a normal parallel test

The integration test first books one of the two double rooms. It then starts
two requests for the same dates and uses a test-only EF command interceptor as
a barrier after both availability queries. Both requests are deliberately put
into the dangerous interleaving while competing for the final double room.

The assertions are:

- exactly one response is `201 Created`;
- exactly one response is `409 Conflict`; and
- persisted overlapping bookings use distinct room IDs.

A test that merely calls `Task.WhenAll` could pass by accident if one request
completed before the other queried availability. The barrier makes the race
repeatable. Using Testcontainers means the test exercises the API, EF-generated
SQL, migrations, retry strategy, and real SQL Server locks rather than relying
on EF InMemory behavior.

### Sensible caveat

Serializable isolation favors correctness but can reduce throughput under
contention. At higher measured load, inspect query plans, lock duration, and
deadlock rates before changing the design. Alternatives include a SQL Server
application lock scoped by hotel or an allocation model that creates
individually constrainable inventory rows. An in-process `lock` or
`SemaphoreSlim` is insufficient because Azure can run more than one API replica.

## Data Model And Database Defences

The main relationships are:

```text
Hotel 1 --- * Room 1 --- * Booking
  |                         |
  +-------------------------+  Booking also stores HotelId
```

Important database configuration includes:

- unique `(HotelId, RoomNumber)` so a hotel cannot have duplicate room numbers;
- unique `BookingReference` as the final uniqueness backstop;
- check constraints for positive room capacity and guest count;
- a check constraint requiring `CheckInDate < CheckOutDate`;
- SQL `date` columns for `DateOnly` values;
- readable string storage for `RoomType`; and
- an overlap-query index on hotel, room, check-in, and checkout.

Capacity against a particular room and non-overlap across date ranges are
cross-row/cross-table business invariants, so ordinary check constraints do not
fully express them. The service and transaction enforce those rules.

One honest database-design limitation is that `Booking` stores both `HotelId`
and `RoomId` with separate foreign keys. The service always selects a room from
the requested hotel, but the database does not itself prove that the two IDs
belong together. In a production hardening pass, either remove the redundant
`Booking.HotelId` or enforce a composite relationship involving room and hotel.

## Testing Story

The current test run passes all 23 tests:

- 9 unit/model tests;
- 14 SQL Server integration/API tests; and
- 0 failures or skipped tests.

Unit tests cover pure boundary rules such as non-past dates, checkout after
check-in, overlap, back-to-back stays, capacity, deterministic ordering, and EF
model configuration.

Integration tests cover migrations, exact seed data, reset and reseed,
availability, same-day and past-date behavior, booking creation and lookup,
Swagger/OpenAPI, and the deterministic concurrency case.

Explain the division like this:

> Unit tests quickly explore boundary conditions in deterministic pure logic.
> Integration tests prove that EF translates the query as expected and that
> SQL Server constraints, migrations, transactions, and HTTP mappings work
> together. I deliberately did not rely only on EF InMemory because it cannot
> validate relational locking behavior.

Useful next integration tests would cover back-to-back bookings and a party
larger than every room through the full HTTP path. Those rules have lower-level
coverage today, but end-to-end coverage would protect the wiring as well.

## API And REST Questions

### Why return a list from hotel search?

> Because the search uses a partial name, it could match more than one hotel.
> For example, searching for "Grand" might return several hotels, so returning
> a list is safer than choosing one result for the user. If no name is provided,
> the endpoint currently returns all hotels. In a larger system, I would also
> add pagination.

### Why is no availability a `409` during booking but `200 []` during search?

> They represent two different situations. The availability search worked
> successfully, but it found no rooms, so returning `200` with an empty list is
> normal. For a booking request, the client is asking the API to create
> something, but the room is no longer available. I return `409 Conflict`
> because the request is valid, but it conflicts with the current booking
> state.

### Why `CreatedAtAction`?

> A successful booking creates a new resource, so `201 Created` is the right
> response. `CreatedAtAction` also adds a `Location` header showing the client
> where it can retrieve that booking, and it returns the booking details in the
> response. That gives the client more useful information than a simple `200`.

### Why no authentication?

> I left authentication out because the challenge explicitly says it is not
> required, and I wanted to keep the solution focused on the booking rules. In
> a real system, I would definitely secure it because booking details contain
> personal data, and the seed and reset endpoints can change or delete data.

### Why use Swagger and predictable IDs?

> I wanted the API to be quick and easy to test. Swagger lets a reviewer try the
> complete flow in a browser, and the seed endpoint always creates the same
> hotel ID and room data. That means they can seed the database and start
> testing immediately without looking up IDs manually or inspecting the
> database.

## Azure And Operational Choices

The optional deployment is deliberately small:

```text
Public immutable GHCR image tagged with commit SHA
        -> Azure Container Apps Consumption, 0-2 replicas
        -> Azure SQL Database serverless, 60-minute auto-pause
```

This mirrors the SQL Server behavior used locally and in tests, supports more
than one API replica, avoids dependence on mutable `latest` images, and keeps
idle cost low. Bicep makes the resource shape repeatable.

Expected trade-offs:

- scale-to-zero and SQL auto-pause create cold-start latency;
- `/health` currently proves process health, not database readiness;
- the seed/reset path applies migrations, which is convenient for the challenge
  but should become a controlled deployment job in production;
- Swagger and public seed/reset endpoints should not be exposed in production;
- SQL currently uses a secret connection string and broad Azure-services
  firewall access; managed identity and tighter networking would be stronger;
- omitting Application Insights and Log Analytics reduced challenge cost, but a
  production service needs structured telemetry, alerts, and auditability.

If asked why Azure was not made more sophisticated, say that hosting was
optional and infrastructure should remain proportionate to the exercise. Then
describe the hardening path instead of pretending the demo setup is production
complete.

## Likely Questions And Strong Answer Points

### “What did you find most challenging?”

> The most challenging part was preventing double bookings when requests arrive
> at the same time. Even with the correct overlap query, two requests could both
> see the same room as available before either one saves. I solved that by
> putting the availability check and booking insert in one short serializable
> transaction. I also added SQL Server retries and an integration test that
> deliberately forces the race. That gave me confidence that the database
> protects the rule, not just that the query looks correct.

### “Why not just put a unique index on room and dates?”

> A unique index can stop exact duplicate values, but it cannot stop different
> date ranges from overlapping. For example, a booking from day 1 to day 3 and
> another from day 2 to day 4 have different dates, but they share a night. SQL
> Server does not have a simple unique constraint for that rule, so I protect
> the availability check with a serializable transaction instead.

### “Why SQL Server instead of SQLite or EF InMemory?”

> I chose SQL Server because the Azure target is Azure SQL, and the highest-risk
> part of this API depends on real database locking and transaction behavior. EF
> InMemory cannot test that, and SQLite behaves differently. Using SQL Server
> locally and in integration tests keeps the environments closer and gives me
> more confidence in the concurrency tests.

### “Why not select one room per night?”

> The requirement says guests must stay in the same room for their whole stay.
> If I selected rooms one night at a time, I might find a different room for
> each night and accidentally require the guests to move. Instead, I only return
> a room when that same room is free for the complete date range.

### “Why choose the smallest suitable room?”

> I choose the smallest room that can hold the group because it keeps larger
> rooms available for larger groups. It also makes room selection predictable,
> which helps testing and reduces inconsistent lock ordering. This was my
> allocation policy rather than an explicit requirement, so in a real product I
> would confirm it with the business and also consider price, accessibility,
> and customer choice.

### “How do you guarantee booking-reference uniqueness?”

> The database unique index is the final guarantee. The service also checks
> whether a generated reference already exists, but that check alone is not
> enough because two requests could generate the same number at the same time.
> In that rare case, the database rejects one insert. For production, I would
> catch that specific error and generate another reference with a bounded retry,
> or use a much larger opaque identifier.

### “What happens if commit succeeds but the response is lost?”

> The client would not know whether the booking was created, so it might retry
> and create a duplicate booking. In production, I would accept an idempotency
> key from the client and store it with a unique index. If the same request is
> sent again, the API can return the original booking instead of creating a new
> one.

### “Would serializable still work with two Container App replicas?”

> Yes, because SQL Server is the shared consistency point for every API replica.
> I did not use an in-memory lock, which would only protect one process. Adding
> replicas could increase database contention, so I would monitor blocking,
> deadlocks, and retries, but the replicas cannot bypass the database
> transaction.

### “Why inject `TimeProvider`?”

> The booking rules depend on what "today" means. By injecting `TimeProvider`, I
> make the clock an explicit dependency instead of calling the current time
> directly throughout the code. That makes date tests predictable and keeps the
> application consistently based on the UTC date.

### “Where is validation performed?”

> I use data annotations at the API boundary for basic request validation, such
> as the guest-name length and guest-count range. I validate business rules,
> such as dates and capacity, in the service layer so those rules still apply if
> the service is called from somewhere other than the controller. There is some
> duplicated availability validation at the moment. My next step would be to
> return a typed result from the service so invalid input and a valid search
> with no rooms are clearly different outcomes.

### “How would this evolve for multiple rooms in one booking?”

> The current model assigns one room to one booking because that is what the
> challenge asks for. For multi-room bookings, I would introduce a reservation
> containing several room allocations. I would also clarify how guests are
> split across rooms, whether all rooms must be booked in one transaction, and
> what should happen when only some rooms are available. I would change the
> model and API rather than trying to hide that behavior inside `GuestCount`.

### “How would cancellation work?”

> I would mark a booking as cancelled rather than deleting it, because keeping
> the history is useful for support and auditing. I would clarify when the room
> becomes available again, make repeated cancellation requests safe, and use
> concurrency control so cancellation cannot conflict with an amendment. I left
> that out because the challenge does not define cancellation rules.

### “What would you monitor?”

> I would monitor response times and error rates for each endpoint, along with
> SQL query time and connection failures. For booking correctness, I would pay
> particular attention to deadlocks, retry counts, and booking conflicts. In
> Azure, I would also watch Container App cold starts and SQL resume time. I
> would use correlation IDs in logs, but I would avoid logging guest names or
> other personal data.

## Honest Improvements, In Priority Order

Frame these as informed next steps, not apologies:

1. Protect or remove seed/reset in deployed environments and move migrations to
   a deployment-time job.
2. Add global exception handling so unexpected failures also use a consistent
   `ProblemDetails` contract.
3. Handle the rare concurrent booking-reference collision with a bounded retry
   and add client-supplied idempotency keys.
4. Add API-level capacity and back-to-back tests, plus focused service tests
   using a controlled `TimeProvider`.
5. Reject whitespace-only guest names after trimming and centralize
   availability validation.
6. Add database-aware readiness separately from the lightweight liveness check.
7. Resolve the redundant `Booking.HotelId` integrity issue with a composite
   relationship or a simpler schema.
8. Use bulk delete for reset, paginate hotel search, define wildcard/search
   semantics, and consider a maximum stay length.
9. Add authentication, authorization, rate limiting, audit logging, and PII
   controls if the exercise becomes a real service.
10. Measure first, then tune the overlap index and transaction strategy for
    higher throughput.

Avoid proposing microservices, queues, event sourcing, or distributed caches
without a concrete requirement. They do not solve the challenge's central
problem and would make consistency harder to explain.

## Suggested Live Demo

Keep the demo under ten minutes and use future dates relative to the interview
date.

1. Open Swagger and `GET /health`.
2. Call `POST /api/admin/seed` and point out the stable hotel ID.
3. Search `GET /api/hotels?name=Grand`.
4. Search availability for two guests and show rooms 201, 202, 301, and 302.
5. Book a double room and point out `201 Created`, `Location`, the `HB-` reference,
   and room 201.
6. Look up the reference with `GET /api/bookings/{reference}`.
7. Repeat availability for the same dates and show room 201 is absent.
8. If time permits, demonstrate that a stay starting on the first booking's
   checkout date can reuse the room.
9. Finish with the concurrency integration test rather than trying to create a
   race manually in Swagger.

Before the interview, verify the deployed environment is still available and
seed it once. Keep the local Docker Compose route ready in case Azure SQL is
resuming, the free environment has changed, or external networking is poor.

## Five-Minute Code Tour

Open files in this order:

1. `BookingRules.cs` — establish date and allocation semantics.
2. `RoomAvailabilityService.cs` — show the capacity and `NOT EXISTS` overlap
   query for one room across the entire stay.
3. `BookingService.cs` — show validation, serializable transaction, execution
   strategy, deterministic selection, insertion, and lookup.
4. `HotelBookingDbContext.cs` — show constraints and indexes as database
   backstops.
5. `ApiEndpointTests.cs` — show the forced concurrency race and HTTP assertions.
6. `Program.cs` — briefly show the intentionally small composition root,
   Swagger, controllers, and health endpoint.

Do not spend the tour reading DTO properties or dependency-registration code
unless asked. Lead with behavior and risk.

## Questions To Ask The Interviewers

Choose two or three that fit the discussion:

- Which part of the solution would you expect to change first in your real
  production environment?
- How does your team normally test database concurrency and integration
  boundaries?
- What booking or allocation rules in the real domain are more complicated
  than this exercise suggests?
- How do you balance delivery speed and architectural boundaries on a small
  service that may grow?
- What does ownership look like after deployment—does the team own its Azure
  infrastructure and operational telemetry as well as the API?

## Final Checklist

- Re-run `dotnet test HotelBooking.slnx --no-restore -m:1` with Docker running.
- Confirm Swagger and `/health` locally and, if using it, in Azure.
- Use future demo dates and keep the seeded hotel ID ready.
- Be able to write the overlap predicate without looking it up.
- Be able to draw the concurrent read/read/write race.
- Know why the database unique index and serializable transaction solve
  different problems.
- Describe at least three deliberate scope choices and three production
  improvements.
- Do not claim the solution is bug-free or fully production-ready; explain why
  it is proportionate, testable, and correct for the central challenge rules.
