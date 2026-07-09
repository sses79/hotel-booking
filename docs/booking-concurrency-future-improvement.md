# Booking Concurrency Future Improvement

## Purpose

The current booking flow is appropriate for this technical challenge:

```text
Start transaction
  -> find one available room
  -> create booking
  -> commit
```

It checks availability inside the transaction, but the transaction currently
uses SQL Server's default isolation level. Under simultaneous load, two requests
could both observe the same room as available before either commits.

This document describes a future production-hardening change. It is not needed
to demonstrate the challenge's core API, but it is the next improvement to make
if concurrent booking correctness becomes a delivery requirement.

## The Race Condition

Assume room 201 has no booking for August 1-3:

```text
Request A                         Request B
---------                         ---------
begin transaction                begin transaction
room 201 appears available       room 201 appears available
select room 201                  select room 201
insert booking A                 insert booking B
commit                           commit
```

The overlap query is correct, but correctness of one query does not guarantee
correctness across two concurrent transactions.

The database has a unique index on booking reference, but it cannot use a normal
unique index to express "no overlapping date ranges for this room." Both
bookings have different IDs and references, so ordinary uniqueness constraints
do not reject the overlap.

## Why Checking Twice Is Not Enough

A possible design is:

```text
check availability
start transaction
check availability again
save
```

The first check is useful only as an early user-facing optimization. The second
check still has a race under `ReadCommitted`: another transaction can make the
same observation before either request inserts.

The transaction's isolation and locking behavior provide the protection, not
the number of times the application repeats the query.

## Recommended Design

Use one availability check inside a short `Serializable` transaction, with
bounded retry handling:

```text
execution strategy / retry boundary
  -> begin Serializable transaction
  -> query candidate rooms and overlapping bookings
  -> choose one room deterministically
  -> insert booking
  -> commit
```

`Serializable` prevents phantom rows for the protected query range. SQL Server
uses key-range locking where supported by the access path, making concurrent
execution behave as though transactions ran one after another.

The transaction must remain short. Do not perform HTTP calls, user interaction,
email, or unrelated work while it is open.

## Proposed EF Core Shape

Use `IDbContextFactory<HotelBookingDbContext>` so each retry starts with clean
EF tracking state. The implementation would conceptually become:

```csharp
await using var strategyContext =
    await dbContextFactory.CreateDbContextAsync(cancellationToken);
var strategy = strategyContext.Database.CreateExecutionStrategy();

return await strategy.ExecuteAsync(async () =>
{
    await using var dbContext =
        await dbContextFactory.CreateDbContextAsync(cancellationToken);

    await using var transaction =
        await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

    var room = await FindFirstAvailableRoomAsync(
        command,
        cancellationToken);

    if (room is null)
    {
        return CreateBookingResult.Failed(
            BookingCreateStatus.NoRoomAvailable,
            "No room is available for the requested stay and guest count.");
    }

    var booking = CreateBooking(command, room);

    dbContext.Bookings.Add(booking);
    await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    return CreateBookingResult.Created(...);
});
```

Required supporting changes:

1. Configure SQL Server with `EnableRetryOnFailure`.
2. Ensure the whole transaction is inside the execution-strategy callback.
3. Verify SQL deadlock error `1205` is retried by the configured strategy, or
   add a small explicit retry policy for that error.
4. Use a fresh `DbContext` for each retry attempt.
5. Keep deterministic room ordering so concurrent requests acquire resources
   in the same order where possible.
6. Limit retry count and use jittered delays.
7. Return `409 Conflict` when retries finish and no room remains.

Do not wrap a user-created transaction outside EF Core's retry strategy.
Retries must replay the complete transactional unit.

## Index And Query Requirements

Serializable isolation is strongest when SQL Server can protect a focused index
range instead of taking broad locks.

The booking lookup needs an index beginning with the room and date fields used
by the overlap predicate. The current model should be reviewed with the actual
query execution plan:

```text
RoomId
CheckInDate
CheckOutDate
```

The existing index also includes `HotelId`. Whether it is ideal depends on the
generated SQL and chosen execution plan.

Before production rollout:

1. Capture the EF-generated SQL.
2. Inspect the Azure SQL execution plan.
3. Confirm an index seek or focused range scan is used.
4. Load-test lock duration and deadlock behavior.
5. Adjust index order only from measured evidence.

Serializable transactions can reduce throughput because competing requests may
block. That is an acceptable trade-off when protecting a small booking
inventory, but it should be measured.

## Retry And Idempotency

Retries introduce a second concern: the connection can fail while commit is in
progress, leaving the client unsure whether the booking was committed.

A production API should accept an idempotency key:

```text
Idempotency-Key: client-generated-unique-value
```

Store that key with a unique database index. A repeated request can then return
the original booking instead of creating another booking.

The booking entity already uses client-generated identifiers, which is helpful,
but a dedicated request key makes the API contract explicit and handles client
retries as well as server retries.

## Alternative: SQL Application Lock

For this small hotel model, SQL Server's `sp_getapplock` is a simpler but more
provider-specific option:

```text
begin transaction
acquire exclusive lock "hotel-booking:{hotelId}"
find room
insert booking
commit and release lock
```

Advantages:

- Easy correctness model.
- Does not depend as heavily on overlap-query range locks.
- Straightforward to test.

Disadvantages:

- Serializes all bookings for one hotel, even when different rooms or dates do
  not conflict.
- Ties the service directly to SQL Server.
- Requires careful timeout and error handling.

For a six-room challenge API, a per-hotel application lock could be a pragmatic
production option. For larger inventory and higher throughput, serializable
transactions with measured indexing provide better concurrency.

## Approaches Not Recommended Alone

### Check Then Recheck

Repeating the query under `ReadCommitted` does not close the race window.

### In-Memory Lock

`lock`, `SemaphoreSlim`, or an in-process dictionary protects only one API
process. Azure Container Apps can run multiple replicas, so another replica can
bypass that lock.

### Optimistic Concurrency Token

A row-version token works when requests update the same existing row. An
availability conflict is often an attempted insert into an empty date range, so
there may be no existing booking row whose version can conflict.

### Booking Reference Unique Index

This protects reference uniqueness, not room/date overlap.

### Distributed Cache Lock

This adds another infrastructure dependency and failure mode. Prefer the SQL
database, which already owns booking consistency, unless measured scale proves
that a separate lock service is necessary.

## Concurrency Test Plan

These tests must use real SQL Server, not EF Core InMemory.

### One Remaining Room

1. Seed one suitable room or pre-book the other suitable rooms.
2. Use a barrier so two requests start together.
3. Send identical booking requests.
4. Assert exactly one request returns `201 Created`.
5. Assert the other returns `409 Conflict`.
6. Assert exactly one overlapping booking exists.

### Two Remaining Rooms

1. Leave two suitable rooms available.
2. Start two requests together.
3. Assert both return `201 Created`.
4. Assert they receive different room IDs.

### Back-To-Back Dates

Run concurrent requests where one checkout equals the other check-in. Both
should succeed because `[checkIn, checkOut)` ranges do not overlap.

### Stress Case

Send more simultaneous requests than available rooms:

```text
20 requests
6 rooms
```

Assert:

- Successful overlapping bookings never exceed the number of suitable rooms.
- Each room receives at most one booking for the requested date range.
- No room/date ranges overlap.
- Remaining requests return a controlled conflict.
- No unhandled deadlock or timeout reaches the client.

Repeat the tests enough times to expose timing-dependent failures.

## Observability

If this becomes production behavior, record:

- Booking attempt count.
- Success and conflict count.
- Retry count.
- Deadlock count.
- Transaction duration.
- Lock timeout count.

This project intentionally does not deploy Application Insights or Log
Analytics. Metrics could be sent to an existing low-cost monitoring system or
temporarily captured during load testing.

## Definition Of Done

The improvement is complete when:

- Booking uses one `Serializable` transaction.
- Retry behavior is bounded and tested.
- Concurrent requests cannot create overlapping bookings.
- Multiple available rooms can still be booked concurrently.
- Back-to-back bookings remain valid.
- API conflicts return `409 ProblemDetails`.
- Integration tests run against disposable SQL Server.
- Query plans and lock behavior have been reviewed.
- The concurrency trade-off is documented in the README.

## References

- [SQL Server transaction locking and row versioning guide](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide)
- [SQL Server transaction isolation levels](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql)
- [EF Core connection resiliency and transaction retries](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)
- [EF Core SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/)
